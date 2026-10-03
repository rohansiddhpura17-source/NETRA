using Microsoft.Extensions.Options;
using NETRA.Application.Abstractions;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Infrastructure;

/// <summary>
/// File storage hardening: extension, size, and path-traversal defences. The client
/// supplied file name is never trusted as a path.
/// </summary>
public sealed class FileStorageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly FileStorageService _service;

    public FileStorageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "netra-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _service = new FileStorageService(
            Options.Create(new FileStorageOptions
            {
                RootPath = _root,
                MaxFileSizeBytes = 1024,
                AllowedExtensions = [".pdf", ".png", ".txt"]
            }),
            contentRootPath: _root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("photo.PNG", true)]
    [InlineData("notes.txt", true)]
    [InlineData("payload.exe", false)]
    [InlineData("script.sh", false)]
    [InlineData("noextension", false)]
    [InlineData("", false)]
    public void Validate_AcceptsOnlyAllowedExtensions(string fileName, bool expectedValid)
    {
        var result = _service.Validate(fileName, "application/octet-stream", 100);

        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void Validate_RejectsEmptyFile()
    {
        var result = _service.Validate("report.pdf", "application/pdf", 0);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("empty");
    }

    [Fact]
    public void Validate_RejectsOversizedFile()
    {
        var result = _service.Validate("report.pdf", "application/pdf", 2048);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("maximum size");
    }

    [Fact]
    public async Task SaveAsync_IgnoresClientFileNameAndGeneratesStoredName()
    {
        using var content = new MemoryStream("hello"u8.ToArray());

        var stored = await _service.SaveAsync(
            content, "../../../etc/passwd.pdf", "application/pdf", "CC-2026-0001");

        stored.StoredFileName.Should().NotContain("passwd");
        stored.StoredFileName.Should().EndWith(".pdf");
        stored.StoragePath.Should().Be(Path.Combine("CC-2026-0001", stored.StoredFileName));
    }

    [Fact]
    public async Task SaveAsync_StaysInsideRootWhenCaseFolderIsTraversal()
    {
        using var content = new MemoryStream("hello"u8.ToArray());

        var stored = await _service.SaveAsync(
            content, "report.pdf", "application/pdf", "../../escape");

        // The folder is collapsed to a single segment, so the stored path has exactly
        // one directory level and no separators survive from the traversal input.
        var segments = stored.StoragePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        segments.Should().HaveCount(2, "the case folder must be reduced to one safe segment");
        segments[0].Should().NotContain(Path.DirectorySeparatorChar.ToString());
        segments[0].Should().NotContain(Path.AltDirectorySeparatorChar.ToString());
        segments[1].Should().EndWith(".pdf");

        // And the file really is inside the storage root.
        var fullPath = Path.GetFullPath(Path.Combine(_root, stored.StoragePath));
        fullPath.StartsWith(Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue("the stored file must resolve inside the storage root");

        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_ComputesChecksumAndSize()
    {
        var payload = "netra evidence"u8.ToArray();
        using var content = new MemoryStream(payload);

        var stored = await _service.SaveAsync(content, "report.pdf", "application/pdf", "case");

        stored.SizeBytes.Should().Be(payload.Length);
        stored.ChecksumSha256.Should().HaveLength(64);
    }

    [Fact]
    public async Task SaveAsync_ThrowsForDisallowedExtension()
    {
        using var content = new MemoryStream("x"u8.ToArray());

        await Assert.ThrowsAsync<FileStorageException>(() =>
            _service.SaveAsync(content, "payload.exe", "application/octet-stream", "case"));
    }

    [Fact]
    public async Task SaveAsync_DoesNotWriteFileWhenValidationFails()
    {
        using var content = new MemoryStream("x"u8.ToArray());

        try
        {
            await _service.SaveAsync(content, "payload.exe", "application/octet-stream", "case");
        }
        catch (FileStorageException)
        {
            // expected
        }

        Directory.GetFiles(_root, "*.exe", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_RemovesStoredFile()
    {
        using var content = new MemoryStream("hello"u8.ToArray());
        var stored = await _service.SaveAsync(content, "report.pdf", "application/pdf", "case");

        var fullPath = Path.Combine(_root, stored.StoragePath);
        File.Exists(fullPath).Should().BeTrue();

        await _service.DeleteAsync(stored.StoragePath);

        File.Exists(fullPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RejectsTraversalPath()
    {
        // A path that escapes the root must be refused rather than deleted.
        await Assert.ThrowsAsync<FileStorageException>(() =>
            _service.DeleteAsync("../../../../etc/hosts"));
    }

    [Fact]
    public async Task DeleteAsync_IgnoresMissingFile()
    {
        await _service.DeleteAsync("case/does-not-exist.pdf");
    }
}