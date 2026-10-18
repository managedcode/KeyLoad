using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeApplicationDisposeFiles
{
    private const int OneByte = 1;
    private const int NoBytes = 0;
    private const string Bound = "The native authority file exceeded its original bound.";

    internal static async Task<Dictionary<string, byte[]>> ReadAsync(string directory,
        long maximumBytes, CancellationToken token)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var store in new[] { PartitionStoreProtocol.CanonicalDirectory, ReplicaProtocol.ReplicaDirectory })
        {
            foreach (var file in new[] { ZoneTreePersistenceFormat.IdentityFileName, ZoneTreePersistenceFormat.JournalFileName })
            {
                var relative = Path.Combine(store, file);
                var bytes = await ReadOneAsync(Path.Combine(directory, relative), maximumBytes, token)
                    .ConfigureAwait(false);
                maximumBytes = checked(maximumBytes - bytes.LongLength);
                result.Add(relative, bytes);
            }
        }
        return result;
    }

    private static async Task<byte[]> ReadOneAsync(string path, long maximumBytes, CancellationToken token)
    {
        var failures = new List<Exception>();
        FileStream? stream = null;
        byte[]? bytes = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (length > maximumBytes || length > int.MaxValue)
            {
                throw new InvalidOperationException(Bound);
            }
            bytes = new byte[checked((int)length)];
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            var tail = new byte[OneByte];
            if (await stream.ReadAsync(tail, token).ConfigureAwait(false) != NoBytes)
            {
                throw new InvalidOperationException(Bound);
            }
        }, failures).ConfigureAwait(false);
        if (stream is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => stream.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return bytes!;
    }

    internal static async Task RequireAsync(Dictionary<string, byte[]> expected,
        Dictionary<string, byte[]> actual)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        foreach (var row in expected)
        {
            await Assert.That(actual.TryGetValue(row.Key, out var bytes)).IsTrue();
            await Assert.That(bytes!.AsSpan().SequenceEqual(row.Value)).IsTrue();
        }
    }
}
