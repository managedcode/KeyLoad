using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.RecoveryTests;

internal static class SampleChunkSnapshotFile
{
    internal static void CaptureExpectedToken(string root, Guid commandId, DatabaseEngine database, PartitionRef partition)
    {
        var expected = CrashDatabase.NextCommitToken(database, partition);
        var bytes = OrleansSerialization.Serialize(expected);
        if (bytes.Length > database.Limits.MaxBatchBytes)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        using var stream = new FileStream(ExpectedTokenPath(root, commandId), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    internal static CommitToken ReadExpectedToken(string root, Guid commandId, int maximumBytes)
    {
        var path = ExpectedTokenPath(root, commandId);
        if (new FileInfo(path).Length > maximumBytes)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > maximumBytes)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        return OrleansSerialization.Deserialize<CommitToken>(bytes);
    }

    private static string ExpectedTokenPath(string root, Guid commandId)
        => Path.Combine(root, $"expected-commit-{commandId:N}.bin");

    internal static async Task WriteAsync<T>(string path, T snapshot, int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (var counting = new SampleChunkSnapshotCountingStream(maximumBytes))
        {
            JsonSerializer.Serialize(counting, snapshot, JsonDefaults.Options);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonDefaults.Serialize(snapshot);
        if (bytes.Length > maximumBytes)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (new FileInfo(path).Length > maximumBytes)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > maximumBytes)
        {
            throw new InvalidOperationException(SampleChunkCrashContract.Invalid);
        }
        return bytes;
    }
}
