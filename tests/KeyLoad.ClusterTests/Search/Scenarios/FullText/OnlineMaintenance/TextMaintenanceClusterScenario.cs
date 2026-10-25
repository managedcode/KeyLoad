namespace KeyLoad.ClusterTests.Search;

internal static class TextMaintenanceClusterScenario
{
    internal const string Collection = "incremental-text";
    internal const string Field = "/text";
    internal const string Consumer = "incremental-text-consumer";
    internal const string Ukrainian = "ukrainian";
    internal const string English = "english";
    internal const string UkrainianJson = """{"text":"привіт світ"}""";
    internal const string EnglishJson = """{"text":"hello world"}""";
    internal const string ChangedJson = """{"text":"оновлено changed"}""";
    private const string Tenant = "incremental-text-tenant";
    private const string DatabasePrefix = "incremental-text-db-";
    private const string Domain = "incremental-text-domain";
    private const string Key = "incremental-text-partition";
    private const string GuidFormat = "N";

    internal static PartitionRef CreatePartition()
        => new(Tenant, DatabasePrefix + Guid.NewGuid().ToString(GuidFormat), Domain, Key);
}
