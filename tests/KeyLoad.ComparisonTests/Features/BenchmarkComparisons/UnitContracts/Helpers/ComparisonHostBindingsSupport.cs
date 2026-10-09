namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Supplies real child-process settings for the caller-binding boundary.</summary>
internal static class ComparisonHostBindingsSupport
{
    private const string NativeWorkloadCase = "NativeClientWorkloadProducesSuccessfulOriginalReports";
    internal const string InvalidEndpointsCode = "KeyLoadComparisonEndpointsInvalid";
    internal const string InvalidRabbitEndpointCode = "RabbitManagementEndpointInvalid";
    internal const string InvalidRabbitCredentialsCode = "RabbitManagementCredentialsInvalid";
    internal const string KeyLoadEndpoint = "Benchmarks__KeyLoadEndpoint";
    internal const string QdrantEndpoint = "Benchmarks__QdrantEndpoint";
    internal const string PeerPrefix = "Benchmarks__KeyLoadEndpoints__";
    internal const string PeerZeroIndex = "0";
    internal const string PeerOneIndex = "1";
    internal const string PeerTwoIndex = "2";
    internal const string QdrantApiKeySetting = "Benchmarks__QdrantApiKey";
    internal const string Neo4jEndpointSetting = "Benchmarks__Neo4jEndpoint";
    internal const string Neo4jPasswordSetting = "Benchmarks__Neo4jPassword";
    internal const string AdminKeySetting = "Benchmarks__AdminKey";
    internal const string PostgresConnectionSetting = "ConnectionStrings__benchmark-postgres";
    internal const string RabbitConnectionSetting = "ConnectionStrings__benchmark-rabbit";
    internal const string RedisConnectionSetting = "ConnectionStrings__benchmark-redis";
    internal const string PostgresImageSetting = "Benchmarks__Images__Postgres";
    internal const string QdrantImageSetting = "Benchmarks__Images__Qdrant";
    internal const string RabbitImageSetting = "Benchmarks__Images__Rabbit";
    internal const string RedisImageSetting = "Benchmarks__Images__Redis";
    internal const string Neo4jImageSetting = "Benchmarks__Images__Neo4j";
    internal const string OutputSetting = "Benchmarks__Output";
    internal const string StorageSetting = "Benchmarks__Storage";
    internal const string DocumentsSetting = "Benchmarks__Documents";
    internal const string OperationsSetting = "Benchmarks__Operations";
    internal const string WarmupSetting = "Benchmarks__Warmup";
    internal const string RepetitionsSetting = "Benchmarks__Repetitions";
    internal const string ConcurrencySetting = "Benchmarks__Concurrency";
    internal const string PayloadBytesSetting = "Benchmarks__PayloadBytes";
    internal const string DimensionsSetting = "Benchmarks__Dimensions";
    internal const string TopKSetting = "Benchmarks__TopK";
    internal const string TimeoutSecondsSetting = "Benchmarks__TimeoutSeconds";
    internal const string GraphVerticesSetting = "Benchmarks__GraphVertices";
    internal const string GraphFanOutSetting = "Benchmarks__GraphFanOut";
    internal const string GraphDepthSetting = "Benchmarks__GraphDepth";
    internal const string RabbitManagementEndpoint = "Benchmarks__RabbitManagementEndpoint";
    internal const string RabbitUser = "Benchmarks__RabbitUser";
    internal const string RabbitPassword = "Benchmarks__RabbitPassword";
    internal const string MissingSettingPrefix = "Missing benchmark setting: ";
    internal const string PrimaryEndpoint = "http://127.0.0.1:1";
    internal const string PeerOneEndpoint = "http://127.0.0.1:2";
    internal const string PeerTwoEndpoint = "http://127.0.0.1:3";
    internal const string QdrantApiKeyValue = "bindings-qdrant-sentinel";
    internal const string AdminKeyValue = "bindings-admin-sentinel";
    internal const string RabbitPasswordValue = "bindings-rabbit-sentinel";
    internal const string RabbitUserValue = "bindings-rabbit-user";
    internal const string InvalidEndpointValue = "https://host.invalid/private-path?token=bindings-url-sentinel";

    internal static Dictionary<string, string> ValidSettings() => new(StringComparer.OrdinalIgnoreCase)
    {
        [KeyLoadEndpoint] = PrimaryEndpoint,
        [QdrantEndpoint] = "http://127.0.0.1:4",
        [PeerPrefix + PeerZeroIndex] = PrimaryEndpoint,
        [PeerPrefix + PeerOneIndex] = PeerOneEndpoint,
        [PeerPrefix + PeerTwoIndex] = PeerTwoEndpoint,
        [RabbitManagementEndpoint] = "http://127.0.0.1:5",
        [RabbitUser] = RabbitUserValue,
        [RabbitPassword] = RabbitPasswordValue,
        [QdrantApiKeySetting] = QdrantApiKeyValue,
        [Neo4jEndpointSetting] = "http://127.0.0.1:6",
        [Neo4jPasswordSetting] = "bindings-neo4j-sentinel",
        [AdminKeySetting] = AdminKeyValue,
        [PostgresConnectionSetting] = "Host=127.0.0.1;Port=0;Username=benchmark;Password=bindings-postgres-sentinel;Database=benchmark;Timeout=1",
        [RabbitConnectionSetting] = "amqp://benchmark:bindings-rabbit-amqp-sentinel@127.0.0.1:0/",
        [RedisConnectionSetting] = "127.0.0.1:0,password=bindings-redis-sentinel,connectTimeout=250,syncTimeout=250,abortConnect=true",
        [PostgresImageSetting] = "postgres-unavailable",
        [QdrantImageSetting] = "qdrant-unavailable",
        [RabbitImageSetting] = "rabbit-unavailable",
        [RedisImageSetting] = "redis-unavailable",
        [Neo4jImageSetting] = "neo4j-unavailable",
        [OutputSetting] = Path.GetTempPath(),
        [StorageSetting] = "temporary-process-test",
        [DocumentsSetting] = "1",
        [OperationsSetting] = "1",
        [WarmupSetting] = "0",
        [RepetitionsSetting] = "1",
        [ConcurrencySetting] = "1",
        [PayloadBytesSetting] = "128",
        [DimensionsSetting] = "2",
        [TopKSetting] = "1",
        [TimeoutSecondsSetting] = "1",
        [GraphVerticesSetting] = "1",
        [GraphFanOutSetting] = "1",
        [GraphDepthSetting] = "1"
    };

    internal static async Task AssertSafeFailureAsync(ComparisonHostExit result, string expectedDetail)
    {
        var output = result.Stdout + result.Stderr;
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(output.Contains(expectedDetail, StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains(NativeWorkloadCase, StringComparison.Ordinal)).IsTrue();
        foreach (var secret in new[]
                 {
                     QdrantApiKeyValue, AdminKeyValue, RabbitPasswordValue,
                     RabbitUserValue, "bindings-neo4j-sentinel", "bindings-postgres-sentinel",
                     "bindings-rabbit-amqp-sentinel", "bindings-redis-sentinel", "bindings-url-sentinel",
                     InvalidEndpointValue
                 })
        {
            await Assert.That(output.Contains(secret, StringComparison.Ordinal)).IsFalse();
        }
    }
}
