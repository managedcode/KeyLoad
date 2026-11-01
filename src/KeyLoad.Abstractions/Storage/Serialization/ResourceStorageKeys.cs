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
    private const int FirstAddressComponent = 1;
    private const int AddressEnd = ResourceComponent + 1;
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

    internal static bool TryCatalog(ReadOnlySpan<byte> key, bool retainScope, out byte[] catalog, out byte[] scope)
    {
        catalog = [];
        scope = [];
        if (!TryComponents(key, out var components))
        {
            return false;
        }
        catalog = Catalog((string)components[FirstAddressComponent]!, (string)components[DatabaseComponent]!,
            (string)components[ResourceComponent]!);
        if (retainScope)
        {
            scope = KeyCodec.Encode(components[FirstAddressComponent..AddressEnd])[KeyCodecTokens.VersionBytes..];
        }
        return true;
    }

    internal static bool TryResource(ReadOnlySpan<byte> key, out (string Tenant, string Database, string Resource) resource)
    {
        resource = (string.Empty, string.Empty, string.Empty);
        if (!TryComponents(key, out var components))
        {
            return false;
        }
        resource = ((string)components[FirstAddressComponent]!, (string)components[DatabaseComponent]!,
            (string)components[ResourceComponent]!);
        return true;
    }

    private static bool TryComponents(ReadOnlySpan<byte> key, out object?[] components)
    {
        components = [];
        if (DataFamilyPrefixLength(key) == 0)
        {
            return false;
        }
        components = KeyCodec.Decode(key);
        return components.Length > ResourceComponent && components[FirstAddressComponent] is string
            && components[DatabaseComponent] is string && components[ResourceComponent] is string;
    }

    internal static int DataFamilyPrefixLength(ReadOnlySpan<byte> key)
    {
        foreach (var prefix in DataPrefixes)
        {
            if (key.StartsWith(prefix))
            {
                return prefix.Length;
            }
        }
        return 0;
    }
}
