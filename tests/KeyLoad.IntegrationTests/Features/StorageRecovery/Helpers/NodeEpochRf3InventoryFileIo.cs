using System.Security.Cryptography;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3InventoryFileIo
{
    internal static async Task CopyFileAsync(string source, string destination, NodeEpochRf3FileEntry expected,
        CancellationToken cancellationToken)
    {
        await using var input = NodeEpochRf3OfflineFiles.OpenRead(source);
        if (input.Length != expected.Length)
        {
            throw new IOException("A node file changed after its inventory was captured.");
        }
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            NodeEpochRf3Protocol.BufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[NodeEpochRf3Protocol.BufferBytes];
        long copied = 0;
        while (copied < expected.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0 || count > expected.Length - copied)
            {
                throw new IOException("A node file changed while being privately copied.");
            }
            digest.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            copied += count;
        }
        if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0
            || Convert.ToHexStringLower(digest.GetHashAndReset()) != expected.Sha256)
        {
            throw new IOException("A node file changed while being privately copied.");
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        NodeEpochRf3OfflineFiles.FlushToDisk(output);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, (UnixFileMode)expected.Mode);
        }
    }

    internal static async Task<string> HashStableAsync(string path, long expectedLength,
        CancellationToken cancellationToken)
    {
        await using var input = NodeEpochRf3OfflineFiles.OpenRead(path);
        if (input.Length != expectedLength)
        {
            throw new IOException("A node file changed during inventory capture.");
        }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[NodeEpochRf3Protocol.BufferBytes];
        long read = 0;
        while (read < expectedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0 || count > expectedLength - read)
            {
                throw new IOException("A node file changed during inventory capture.");
            }
            digest.AppendData(buffer, 0, count);
            read += count;
        }
        if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new IOException("A node file grew during inventory capture.");
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }
}
