namespace NETRA.Application.Abstractions;

/// <summary>Result of persisting an uploaded file.</summary>
public sealed class StoredFileResult
{
    public StoredFileResult(string storagePath, string storedFileName, string checksumSha256, long sizeBytes)
    {
        StoragePath = storagePath;
        StoredFileName = storedFileName;
        ChecksumSha256 = checksumSha256;
        SizeBytes = sizeBytes;
    }

    /// <summary>Relative path recorded in the database.</summary>
    public string StoragePath { get; }

    /// <summary>Sanitised name actually written to storage.</summary>
    public string StoredFileName { get; }

    public string ChecksumSha256 { get; }

    public long SizeBytes { get; }
}

public sealed class FileValidationResult
{
    private FileValidationResult(bool isValid, string? error, string extension)
    {
        IsValid = isValid;
        Error = error;
        Extension = extension;
    }

    public bool IsValid { get; }

    public string? Error { get; }

    public string Extension { get; }

    public static FileValidationResult Valid(string extension) => new(true, null, extension);

    public static FileValidationResult Invalid(string error) => new(false, error, string.Empty);
}

/// <summary>
/// Stores evidence files outside the web root. Implementations must never trust the
/// client-supplied file name: they generate the stored name and confine writes to a
/// configured root directory.
/// </summary>
public interface IFileStorageService
{
    /// <summary>Allowed evidence file extensions.</summary>
    IReadOnlyCollection<string> AllowedExtensions { get; }

    /// <summary>Maximum permitted upload size in bytes.</summary>
    long MaxFileSizeBytes { get; }

    /// <summary>
    /// Validates a candidate upload without writing anything.
    /// </summary>
    FileValidationResult Validate(string fileName, string contentType, long length);

    /// <summary>
    /// Validates and persists an upload. Throws <see cref="FileStorageException"/>
    /// when validation fails so callers must handle it.
    /// </summary>
    Task<StoredFileResult> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        string caseFolder,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a previously stored file. Missing files are ignored.</summary>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a previously stored file for reading. Returns null when the file is
    /// missing. The relative path is confined to the storage root; callers must
    /// authorise before exposing content and must never accept user-supplied paths.
    /// </summary>
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}

public sealed class FileStorageException : Exception
{
    public FileStorageException(string message) : base(message)
    {
    }
}