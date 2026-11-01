namespace KeyLoad.Storage;

/// <summary>Identifies table data and its canonical resource catalog record for physical storage routing.</summary>
internal static class ResourceStorageKeys
{
    internal const string CatalogFamily = "catalog";
    internal const string DocumentFamily = "document";
    internal const string IndexFamily = "index";
    internal const string UniqueFamily = "unique";
    internal const string DocumentEpochFamily = "document-epoch";
    internal const string VisibilityEpochFamily = "visibility-epoch";
    internal const string VectorFamily = "vector";
    internal const string VectorLineageFamily = "vector-projection-lineage";
    internal const string VectorEffectFamily = "vector-projection-effect";
    internal const string Bm25Family = "bm25-corpus-current";
    internal const string OnlineTextFamily = "online-text-current";
    internal const string AnalyticsEventFamily = "analytics-event";
    internal const string AnalyticsGenerationFamily = "analytics-generation";
    private const int ResourceComponent = 5;
    private const int DatabaseComponent = 2;
    private static readonly byte[][] DataPrefixes =
    [
        .. new[]
        {
            DocumentFamily, IndexFamily, UniqueFamily, DocumentEpochFamily, VisibilityEpochFamily, VectorFamily,
            VectorLineageFamily, VectorEffectFamily, Bm25Family, OnlineTextFamily,
            AnalyticsEventFamily, AnalyticsGenerationFamily
        }.Concat(global::KeyLoad.TimeSeries.TimeSeriesStorageFamilies.All).Select(static family => KeyCodec.Encode(family))
    ];

    internal static byte[] Catalog(string tenant, string database, string resource)
        => KeyCodec.Encode(CatalogFamily, tenant, database, resource);

    internal static bool TryCatalog(ReadOnlySpan<byte> key, out byte[] catalog)
    {
        catalog = [];
        if (!TryResource(key, out var resource))
        {
            return false;
        }
        catalog = Catalog(resource.Tenant, resource.Database, resource.Resource);
        return true;
    }

    internal static bool TryResource(ReadOnlySpan<byte> key, out (string Tenant, string Database, string Resource) resource)
    {
        resource = (string.Empty, string.Empty, string.Empty);
        if (!IsTableData(key))
        {
            return false;
        }
        var components = KeyCodec.Decode(key);
        if (components.Length <= ResourceComponent || components[1] is not string tenant
            || components[DatabaseComponent] is not string database || components[ResourceComponent] is not string name)
        {
            return false;
        }
        resource = (tenant, database, name);
        return true;
    }

    private static bool IsTableData(ReadOnlySpan<byte> key)
    {
        foreach (var prefix in DataPrefixes)
        {
            if (key.StartsWith(prefix))
            {
                return true;
            }
        }
        return false;
    }
}
