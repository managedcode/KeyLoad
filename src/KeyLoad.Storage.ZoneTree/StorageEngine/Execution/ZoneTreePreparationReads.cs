
namespace KeyLoad.Storage.ZoneTree;

/// <summary>Reads execution committed bytes without admitting or publishing disposable point-cache entries.</summary>
internal static class ZoneTreePreparationReads
{
    internal static byte[]? ReadOwnedValue(ZoneTreeStoreRuntime runtime, byte[] key)
    {
        byte[]? result = null;
        Read(runtime, key, bytes => result = bytes.ToArray(), null, owned: true);
        return result;
    }

    internal static bool ReadValue(ZoneTreeStoreRuntime runtime, byte[] key,
        StorageValueReader reader, StorageReadObserver? observer)
        => Read(runtime, key, reader, observer, owned: false);

    private static bool Read(ZoneTreeStoreRuntime runtime, byte[] key,
        StorageValueReader reader, StorageReadObserver? observer, bool owned)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        Memory<byte> memory = key;
        using var snapshot = runtime.Tree.BeginReadOnlyTransaction();
        var found = snapshot.TryGet(memory, out var value);
        if (!found)
        {
            runtime.ReadCounters.Point(owned, key.Length);
            observer?.Invoke(key.Length);
            return false;
        }
        var bytes = value.Span;
        var length = (long)key.Length + bytes.Length;
        runtime.ReadCounters.Point(owned, length);
        observer?.Invoke(length);
        reader(bytes);
        return true;
    }
}
