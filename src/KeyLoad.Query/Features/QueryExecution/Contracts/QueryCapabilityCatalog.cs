namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Defines the immutable Q1 capability vocabulary and exposes the current operation limits.</summary>
internal static class QueryCapabilityCatalog
{
    private const int ManifestVersion = 1;
    private const string QueryDialectVersion = "Q1";
    private const string AtomicPartitionScope = "atomicPartition";
    private const string DecimalScalarType = "decimal";
    private const string MissingValueSemantics = "distinctFromNull";
    private const string SqlCallerSurface = "SQL";
    private const string JsonCallerSurface = "JSON";
    private const string CSharpCallerSurface = "C#";
    private const string ComparisonCapability = "comparison";
    private const string AndCapability = "AND";
    private const string OrCapability = "OR";
    private const string NotCapability = "NOT";
    private const string InCapability = "IN";
    private const string BetweenCapability = "BETWEEN";
    private const string NotBetweenCapability = "NOT BETWEEN";
    private const string NullCapability = "IS NULL";
    private const string MissingCapability = "IS MISSING";
    private const string DocumentChangeFeedCapability = "documentChangeFeed";
    private const string ScalarLiveQueryCapability = "scalarLiveQuery";
    private const string ModelViewsCapability = "modelViewsV1";

    internal static QueryCapabilityManifest Create(DatabaseLimits limits) => new(ManifestVersion, ManifestVersion, QueryDialectVersion, AtomicPartitionScope, DecimalScalarType, MissingValueSemantics,
        [SqlCallerSurface, JsonCallerSurface, CSharpCallerSurface], [ComparisonCapability, AndCapability, OrCapability, NotCapability, InCapability, BetweenCapability, NotBetweenCapability, NullCapability, MissingCapability],
        limits.MaxResults, limits.MaxScanRecords, limits.MaxQueryBytes, limits.MaxQueryDepth, true, true,
        limits.MaxQueryReadBytes, [QueryDialectVersion, DocumentChangeFeedCapability, ScalarLiveQueryCapability, ModelViewsCapability, SqlGraphSearchSyntax.ProfileName, SqlGraphPathSyntax.VersionProfile]);
}
