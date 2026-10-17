using KeyLoad.Artifacts;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupPieceBoundaryAssertions
{
    private const string ManifestName = "backup.json";
    private const string JournalName = "commands.wal";
    private const string IdentityName = "identity.json";
    private const int SinglePiece = 1;
    private const string InvalidPieceDetail = "The backup archive piece size is invalid.";

    internal static async Task ArchiveAsync(string artifact, IReadOnlyDictionary<string, byte[]> files,
        int piece, int journalPieces)
    {
        await Assert.That(files[ManifestName].Length).IsLessThanOrEqualTo(piece);
        await Assert.That(files[IdentityName].Length).IsLessThanOrEqualTo(piece);
        (string Name, long Bytes, int Pieces)[] expected =
        [
            (ManifestName, files[ManifestName].LongLength, SinglePiece),
            (JournalName, files[JournalName].LongLength, journalPieces),
            (IdentityName, files[IdentityName].LongLength, SinglePiece)
        ];
        await Assert.That(BackupArtifact.Inspect(artifact)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    internal static async Task RejectedAsync(CliBackupRestoreProcessResult result, string artifact)
    {
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.StandardOutput).IsEqualTo(string.Empty);
        await Assert.That(result.StandardError.Contains(InvalidPieceDetail, StringComparison.Ordinal)).IsTrue();
        await Assert.That(File.Exists(artifact)).IsFalse();
    }

    internal static async Task UnchangedAsync(string artifact, byte[] original, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(artifact, token);
        await Assert.That(bytes.AsSpan().SequenceEqual(original)).IsTrue();
    }
}
