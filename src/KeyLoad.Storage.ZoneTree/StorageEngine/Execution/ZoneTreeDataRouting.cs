namespace KeyLoad.Storage.ZoneTree;

/// <summary>Resolves table data from the canonical transactional resource catalog; outcomes never use the plain tree.</summary>
internal static class ZoneTreeDataRouting
{
    internal static bool IsPlain(ZoneTreeStoreRuntime runtime, ReadOnlySpan<byte> key)
    {
        if (!ResourceStorageKeys.TryCatalog(key, out var catalog))
        {
            return false;
        }
        var snapshot = runtime.CurrentOperation.Snapshot.Transaction;
        return snapshot.TryGet(catalog, out var value)
            && !OrleansSerialization.Deserialize<ResourceDefinition>(value.Span).Transactional;
    }
}
