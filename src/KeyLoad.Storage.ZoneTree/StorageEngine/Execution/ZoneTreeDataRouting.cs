namespace KeyLoad.Storage.ZoneTree;

/// <summary>Resolves table data from the canonical transactional resource catalog; outcomes never use the plain tree.</summary>
internal static class ZoneTreeDataRouting
{
    internal static bool IsPlain(ZoneTreeStoreRuntime runtime, ReadOnlySpan<byte> key)
    {
        var operation = runtime.CurrentOperation;
        var familyLength = ResourceStorageKeys.DataFamilyPrefixLength(key);
        if (operation.TryReadRouting(key, familyLength, out var plain))
        {
            return plain;
        }
        if (!ResourceStorageKeys.TryCatalog(key, !operation.Writable, out var catalog, out var scope))
        {
            return false;
        }
        var snapshot = operation.Snapshot.Transaction;
        plain = snapshot.TryGet(catalog, out var value)
            && !OrleansSerialization.Deserialize<ResourceDefinition>(value.Span).Transactional;
        operation.BindReadRouting(scope, plain);
        return plain;
    }
}
