using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class MetadataOuterChecksumRepairAssertions
{
    private const string ErrorCodeProperty = "errorCode";
    private const string DetailProperty = "detail";
    private const string CorruptionCode = "Corruption";
    private const int FailedExit = 1;
    private const long RestoreCommitCount = 1;
    private const string MissingName = "The original owned metadata path has no file name.";

    internal static async Task RefusedCliAsync(CliBackupRestoreProcessResult result, string destination)
    {
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(FailedExit);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
        await Assert.That(File.Exists(destination) || Directory.Exists(destination)).IsFalse();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(CorruptionCode);
        await Assert.That(problem.RootElement.GetProperty(DetailProperty).GetString())
            .IsEqualTo(MetadataTestContract.BackupFileVerificationFailedDetail);
    }

    internal static async Task RestoredAsync(MetadataBackupFixture fixture, string destination,
        CliBackupRestoreProcessResult result, long sourcePosition, CancellationToken token)
    {
        var actual = BackupJournalRepairAssertions.Read(destination, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, actual.Identity);
        await Assert.That(actual.Identity.NodeId).IsNotEqualTo(fixture.OriginalIdentity.NodeId);
        await Assert.That(actual.Identity.Incarnation).IsNotEqualTo(fixture.OriginalIdentity.Incarnation);
        await Assert.That(actual.Identity.SigningKey.Span.SequenceEqual(fixture.OriginalIdentity.SigningKey.Span)).IsFalse();
        await Assert.That(actual.Identity.Durability).IsEqualTo(fixture.OriginalIdentity.Durability);
        await Assert.That(actual.Identity.DispatchPaused).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(sourcePosition + RestoreCommitCount);
        string[] expected =
        [
            Convert.ToHexString(MetadataBackupFixture.StoredKeyBytes) + ":" +
                Convert.ToHexString(OrleansSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.DispatchPausedKey)) + ":" +
                Convert.ToHexString(OrleansSerialization.Serialize(true))
        ];
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
    }

    internal static async Task OriginalArchiveAsync(Dictionary<string, byte[]> original, string backup,
        string destination, CancellationToken token)
    {
        var files = Directory.EnumerateFiles(backup).Select(path => Path.GetFileName(path)
            ?? throw new InvalidOperationException(MissingName)).ToArray();
        await Assert.That(files).IsEquivalentTo(original.Keys);
        await Assert.That(Directory.EnumerateDirectories(backup).Select(path => Path.GetFileName(path)
            ?? throw new InvalidOperationException(MissingName)).ToArray())
            .IsEquivalentTo(new[] { Path.GetFileName(destination) ?? throw new InvalidOperationException(MissingName) });
        foreach (var row in original)
        {
            var current = await File.ReadAllBytesAsync(Path.Combine(backup, row.Key), token);
            await Assert.That(current.AsSpan().SequenceEqual(row.Value)).IsTrue();
        }
    }
}
