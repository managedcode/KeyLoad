using KeyLoad.Core;

namespace KeyLoad.ProcessRecoveryTests;

internal static class SampleChunkSnapshotFile
{
    internal static void CaptureExpectedToken(string root, Guid commandId, DatabaseEngine database, PartitionRef partition)
    {
        var expected = CrashDatabase.NextCommitToken(database, partition);
        var bytes = OrleansSerialization.Serialize(expected);
        using var stream = new FileStream(ExpectedTokenPath(root, commandId), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    internal static CommitToken ReadExpectedToken(string root, Guid commandId)
        => OrleansSerialization.Deserialize<CommitToken>(File.ReadAllBytes(ExpectedTokenPath(root, commandId)));

    private static string ExpectedTokenPath(string root, Guid commandId)
        => Path.Combine(root, $"expected-commit-{commandId:N}.bin");

    internal static async Task WriteAsync<T>(string path, T snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonDefaults.Serialize(snapshot);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
