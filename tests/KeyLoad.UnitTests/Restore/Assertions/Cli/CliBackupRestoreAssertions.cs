using System.Text.Json;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CliBackupRestoreAssertions
{
    private const string EntryNameProperty = "name";
    private const string EntryBytesProperty = "bytes";
    private const string EntryPiecesProperty = "pieces";
    private const string RestoreNodeProperty = "nodeId";
    private const string RestoreIncarnationProperty = "incarnation";
    private const string RestoreDispatchPausedProperty = "dispatchPaused";
    private static readonly string[] CanonicalEntryNames = ["backup.json", "commands.wal", "identity.json"];

    internal static async Task SuccessfulProcessAsync(CliBackupRestoreProcessResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardError).IsEmpty();
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
    }

    internal static async Task RejectedProcessAsync(CliBackupRestoreProcessResult result)
    {
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.StandardError).IsNotEqualTo(string.Empty);
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
    }

    internal static async Task InspectionAsync(string json, IReadOnlyDictionary<string, byte[]> expectedFiles)
    {
        using var document = JsonDocument.Parse(json);
        var entries = document.RootElement.EnumerateArray().ToArray();
        var names = entries.Select(entry => entry.GetProperty(EntryNameProperty).GetString()).ToArray();
        await Assert.That(names.SequenceEqual(CanonicalEntryNames)).IsTrue();
        await Assert.That(expectedFiles.Keys.SequenceEqual(CanonicalEntryNames, StringComparer.Ordinal)).IsTrue();
        foreach (var entry in entries)
        {
            var name = entry.GetProperty(EntryNameProperty).GetString();
            var expected = name is null ? null : expectedFiles.GetValueOrDefault(name);
            await Assert.That(expected is not null).IsTrue();
            if (expected is not null)
            {
                await Assert.That(entry.GetProperty(EntryBytesProperty).GetInt64()).IsEqualTo(expected.LongLength);
            }
            await Assert.That(entry.GetProperty(EntryPiecesProperty).GetInt32() > 0).IsTrue();
        }
    }

    internal static async Task RestoredStoreAsync(CliBackupRestoreFixture fixture,
        CliBackupRestoreProcessResult result, string destination)
    {
        using var receipt = JsonDocument.Parse(result.StandardOutput);
        var restoredNode = receipt.RootElement.GetProperty(RestoreNodeProperty).GetGuid();
        var restoredIncarnation = receipt.RootElement.GetProperty(RestoreIncarnationProperty).GetGuid();
        await Assert.That(receipt.RootElement.GetProperty(RestoreDispatchPausedProperty).GetBoolean()).IsTrue();
        await Assert.That(restoredIncarnation).IsNotEqualTo(fixture.OriginalIdentity.Incarnation);
        using var reopened = new ZoneTreeStore(new(destination), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Identity.NodeId).IsEqualTo(restoredNode);
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(restoredIncarnation);
        await Assert.That(reopened.Identity.DispatchPaused).IsTrue();
        var value = reopened.Read(view => NativeSerialization.Deserialize<string>(view.ReadOwnedValue(CliBackupRestoreFixture.StoredKeyBytes)!));
        await Assert.That(value).IsEqualTo(CliBackupRestoreFixture.StoredValue);
    }

    internal static async Task FilesEqualAsync(IReadOnlyDictionary<string, byte[]> expected, string directory,
        CancellationToken cancellationToken)
    {
        var actual = await CaptureFilesAsync(directory, cancellationToken).ConfigureAwait(false);
        await Assert.That(actual.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected.Keys.Order(StringComparer.Ordinal))).IsTrue();
        foreach (var entry in expected)
        {
            await Assert.That(actual[entry.Key].AsSpan().SequenceEqual(entry.Value)).IsTrue();
        }
    }

    internal static async Task<SortedDictionary<string, byte[]>> CaptureFilesAsync(string directory,
        CancellationToken cancellationToken)
    {
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            result.Add(Path.GetRelativePath(directory, path), await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
        }
        return result;
    }
}
