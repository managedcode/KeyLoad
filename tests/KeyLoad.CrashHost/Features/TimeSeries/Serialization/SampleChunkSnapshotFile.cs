using System.Text.Json;

namespace KeyLoad.CrashHost;

internal static class SampleChunkSnapshotFile
{
    internal static async Task WriteAsync<T>(string path, T snapshot, int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (var counting = new SampleChunkSnapshotCountingStream(maximumBytes))
        { JsonSerializer.Serialize(counting, snapshot, JsonDefaults.Options); }
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonDefaults.Serialize(snapshot);
        if (bytes.Length > maximumBytes) { throw new InvalidOperationException(SampleChunkCrashContract.Invalid); }
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (new FileInfo(path).Length > maximumBytes)
        { throw new InvalidOperationException(SampleChunkCrashContract.Invalid); }
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > maximumBytes) { throw new InvalidOperationException(SampleChunkCrashContract.Invalid); }
        return bytes;
    }
}
