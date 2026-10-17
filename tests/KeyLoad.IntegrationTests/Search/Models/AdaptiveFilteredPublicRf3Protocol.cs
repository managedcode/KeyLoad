namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Protocol
{
    internal const int Count = 1_000;
    internal const int BatchDocuments = 100;
    internal const int Limit = 4;
    internal const int Fusion = 60;
    internal const int Version = 1;
    internal const int ExactThreshold = 256;
    internal const long Revision = 1;
    internal const string IdPrefix = "adaptive-";
    internal const string IdFormat = "D4";
    internal const string Body = "{}";
    internal const string FieldGrant = "adaptive-filter.vector.use";
    internal const string Classification = "adaptive-filter-vector";
    internal const string Missing = "adaptive-missing";
    internal const string Tool = "keyload_search_ann_read";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string FieldDenied = "The query requires a protected field-use grant.";
    internal const string SafeDenied = "The authenticated principal is not permitted to perform this operation.";
    internal const Capability Healthy = Capability.Query | Capability.DocumentsRead | Capability.VectorSearch;
    internal static string Id(int ordinal) => IdPrefix + ordinal.ToString(IdFormat, System.Globalization.CultureInfo.InvariantCulture);
}
