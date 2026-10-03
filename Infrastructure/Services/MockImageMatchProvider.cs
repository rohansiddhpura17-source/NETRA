using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.ImageInvestigation;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// The only match provider shipped with NETRA. It is a controlled academic/demo
/// component: it compares the uploaded file's SHA-256 checksum against the checksums
/// of images NETRA already stores and derives a deterministic pseudo-similarity from
/// the combined digest. It performs no image or facial analysis, contacts no external
/// or biometric system, and its scores are investigative leads, never identity
/// conclusions. The same upload always produces the same candidate set and scores.
/// </summary>
public sealed class MockImageMatchProvider : IImageMatchProvider
{
    /// <summary>Scores are bounded well below 1.0 to reinforce that no match is certain.</summary>
    private const decimal ScoreFloor = 0.40m;
    private const decimal ScoreCeiling = 0.95m;

    private readonly NetraDbContext _context;

    public MockImageMatchProvider(NetraDbContext context)
    {
        _context = context;
    }

    public string Name => "Deterministic demo provider (synthetic)";

    public async Task<ImageMatchResult> FindCandidatesAsync(
        ImageMatchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.SourceChecksumSha256) || query.MaxCandidates <= 0)
        {
            return new ImageMatchResult
            {
                Candidates = Array.Empty<ImageProviderCandidate>()
            };
        }

        var max = Math.Min(query.MaxCandidates, ImageInvestigationLimits.MaxCandidates);

        var stored = await _context.ImageRecords.AsNoTracking()
            .Where(record => record.Id != query.SourceImageRecordId
                             && record.ChecksumSha256 != query.SourceChecksumSha256)
            .OrderBy(record => record.ChecksumSha256)
            .ThenBy(record => record.Id)
            .Select(record => new { record.Id, record.ChecksumSha256 })
            .ToListAsync(cancellationToken);

        // Deterministic pseudo-similarity: derived only from the two checksums, so
        // the same pair always scores the same. A stable pre-sort keeps the result
        // independent of database ordering before the score ordering is applied.
        var scored = stored
            .Select(record => new ImageProviderCandidate
            {
                MatchedImageRecordId = record.Id,
                SimilarityScore = ComputeScore(query.SourceChecksumSha256, record.ChecksumSha256)
            })
            .OrderByDescending(candidate => candidate.SimilarityScore)
            .ThenBy(candidate => candidate.MatchedImageRecordId)
            .Take(max)
            .ToList();

        return new ImageMatchResult { Candidates = scored };
    }

    private static decimal ComputeScore(string sourceChecksum, string candidateChecksum)
    {
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{sourceChecksum}:{candidateChecksum}"));

        // First four bytes as an unsigned integer mapped onto [floor, ceiling].
        // The division must be floating point: integer division would always yield 0.
        var raw = ((uint)digest[0] << 24 | (uint)digest[1] << 16
                   | (uint)digest[2] << 8 | digest[3]) / (double)uint.MaxValue;

        return Math.Round(
            ScoreFloor + (decimal)raw * (ScoreCeiling - ScoreFloor), 4,
            MidpointRounding.AwayFromZero);
    }
}
