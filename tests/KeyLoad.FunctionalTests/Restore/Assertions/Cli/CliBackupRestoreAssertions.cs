using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class CliBackupRestoreAssertions
{
    private const string RestoreNodeProperty = "nodeId";
    private const string RestoreIncarnationProperty = "incarnation";
    private const string RestoreDispatchPausedProperty = "dispatchPaused";
    private const string TreeDirectoryName = "tree";
    private const string PlainTreeDirectoryName = "plain-tree";

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

    /// <summary>Requires the ZoneTree backup layout: the store identity plus the backed-up tree.</summary>
    internal static async Task BackupLayoutAsync(IReadOnlyDictionary<string, byte[]> files)
    {
        await Assert.That(files.ContainsKey(ZoneTreePersistenceFormat.IdentityFileName)).IsTrue();
        await Assert.That(files.Keys.Where(name => name != ZoneTreePersistenceFormat.IdentityFileName)
            .All(name => name.StartsWith(TreeDirectoryName + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || name.StartsWith(PlainTreeDirectoryName + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .IsTrue();
        await Assert.That(files.Keys.Any(name => name.StartsWith(PlainTreeDirectoryName + Path.DirectorySeparatorChar,
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(files.Count > 1).IsTrue();
    }

    internal static async Task ReceiptAsync(CliBackupRestoreProcessResult result, StoreIdentity identity)
    {
        using var receipt = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(receipt.RootElement.GetProperty(RestoreNodeProperty).GetGuid()).IsEqualTo(identity.NodeId);
        await Assert.That(receipt.RootElement.GetProperty(RestoreIncarnationProperty).GetGuid()).IsEqualTo(identity.Incarnation);
        await Assert.That(receipt.RootElement.GetProperty(RestoreDispatchPausedProperty).GetBoolean()).IsTrue();
        await Assert.That(identity.DispatchPaused).IsTrue();
    }

    internal static async Task RestoredStoreAsync(CliBackupRestoreFixture fixture,
        CliBackupRestoreProcessResult result, string destination)
    {
        using var receipt = JsonDocument.Parse(result.StandardOutput);
        var restoredNode = receipt.RootElement.GetProperty(RestoreNodeProperty).GetGuid();
        var restoredIncarnation = receipt.RootElement.GetProperty(RestoreIncarnationProperty).GetGuid();
        await Assert.That(receipt.RootElement.GetProperty(RestoreDispatchPausedProperty).GetBoolean()).IsTrue();
        await Assert.That(restoredIncarnation).IsNotEqualTo(fixture.OriginalIdentity.Incarnation);
        using var reopened = new ZoneTreeStore(new(destination), UnitExecutionOptions.StorageExecution());
        await Assert.That(reopened.Identity.NodeId).IsEqualTo(restoredNode);
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(restoredIncarnation);
        await Assert.That(reopened.Identity.DispatchPaused).IsTrue();
        var value = reopened.Read(view => OrleansSerialization.Deserialize<string>(view.ReadOwnedValue(CliBackupRestoreFixture.StoredKeyBytes)!));
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
