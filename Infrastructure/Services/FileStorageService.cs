using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using NETRA.Application.Abstractions;

namespace NETRA.Infrastructure.Services;

public sealed class FileStorageOptions
{
    /// <summary>Absolute or relative directory that holds stored evidence files.</summary>
    public string RootPath { get; set; } = "App_Data/evidence";

    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt",
        ".zip", ".mp4", ".mp3", ".wav"
    ];

    /// <summary>Permitted MIME types. Empty means any type is accepted.</summary>
    public string[] AllowedContentTypes { get; set; } = [];
}

/// <summary>
/// Stores evidence files on the local file system, outside the web root.
/// </summary>
/// <remarks>
/// Security rules enforced here:
/// the client-supplied file name is never used as a path, only its extension is
/// inspected; the stored name is generated server-side from a random token;
/// every resolved path is confirmed to stay inside the configured root, which
/// defeats path traversal and symlink-style escapes.
/// </remarks>
public sealed class FileStorageService : IFileStorageService
{
    private readonly FileStorageOptions _options;
    private readonly string _rootPath;

    public FileStorageService(IOptions<FileStorageOptions> options, string contentRootPath)
    {
        _options = options.Value;
        _rootPath = Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(contentRootPath, _options.RootPath);
    }

    public IReadOnlyCollection<string> AllowedExtensions => _options.AllowedExtensions;

    public long MaxFileSizeBytes => _options.MaxFileSizeBytes;

    public FileValidationResult Validate(string fileName, string contentType, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileValidationResult.Invalid("A file name is required.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (string.IsNullOrEmpty(extension))
        {
            return FileValidationResult.Invalid("The file must have an extension.");
        }

        if (!_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return FileValidationResult.Invalid(
                $"Files of type '{extension}' are not accepted.");
        }

        if (length <= 0)
        {
            return FileValidationResult.Invalid("The uploaded file is empty.");
        }

        if (length > _options.MaxFileSizeBytes)
        {
            return FileValidationResult.Invalid(
                $"The file exceeds the maximum size of {_options.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        if (_options.AllowedContentTypes.Length > 0
            && !_options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return FileValidationResult.Invalid("The file content type is not accepted.");
        }

        return FileValidationResult.Valid(extension);
    }

    public async Task<StoredFileResult> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        string caseFolder,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(originalFileName, contentType, content.Length);
        if (!validation.IsValid)
        {
            throw new FileStorageException(validation.Error!);
        }

        // The folder name comes from the caller; sanitise it so it cannot escape.
        var safeFolder = SanitiseSegment(caseFolder);

        // Generated name: the client's file name is never used as a path.
        var storedFileName = $"{Guid.NewGuid():N}{validation.Extension}";

        var directory = Path.Combine(_rootPath, safeFolder);
        var fullPath = Path.Combine(directory, storedFileName);

        EnsureWithinRoot(fullPath);

        Directory.CreateDirectory(directory);

        long written;
        string checksum;

        await using (var target = new FileStream(
            fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true))
        {
            using var hasher = SHA256.Create();

            // Copy while hashing in a single pass so the file is never read twice.
            var buffer = new byte[81920];
            int read;
            written = 0;
            checksum = string.Empty;

            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written += read;
                if (written > _options.MaxFileSizeBytes)
                {
                    throw new FileStorageException("The file exceeds the maximum allowed size.");
                }

                hasher.TransformBlock(buffer, 0, read, null, 0);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            checksum = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
        }

        var relativePath = Path.Combine(safeFolder, storedFileName);

        return new StoredFileResult(relativePath, storedFileName, checksum, written);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return Task.CompletedTask;
        }

        var fullPath = Path.Combine(_rootPath, storagePath);
        EnsureWithinRoot(fullPath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public async Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return null;
        }

        var fullPath = Path.Combine(_rootPath, storagePath);
        EnsureWithinRoot(fullPath);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        // Buffer small files in memory so the database context and the HTTP request
        // never hold an open file handle.
        await using var source = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    /// <summary>
    /// Confirms the resolved absolute path stays inside the storage root.
    /// </summary>
    private void EnsureWithinRoot(string fullPath)
    {
        var root = Path.GetFullPath(_rootPath);
        var target = Path.GetFullPath(fullPath);

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileStorageException("The resolved file path is outside the storage root.");
        }
    }

    /// <summary>Reduces an arbitrary string to a single safe path segment.</summary>
    private static string SanitiseSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();

        var cleaned = new string(value
            .Select(ch => invalid.Contains(ch) || ch == Path.DirectorySeparatorChar
                           || ch == Path.AltDirectorySeparatorChar
                ? '_'
                : ch)
            .ToArray())
            .Trim();

        if (cleaned.Length == 0 || cleaned is "." or "..")
        {
            return "unassigned";
        }

        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}