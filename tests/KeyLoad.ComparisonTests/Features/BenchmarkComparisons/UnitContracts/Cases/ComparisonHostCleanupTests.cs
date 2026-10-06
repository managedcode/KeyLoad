using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Exercises target cleanup failure through the real comparison host process.</summary>
internal sealed class ComparisonHostCleanupTests
{
    private const string KeyLoadEndpointKey = "Benchmarks__KeyLoadEndpoint";
    private const string KeyLoadPeerZeroKey = "Benchmarks__KeyLoadEndpoints__" + ComparisonHostBindingsSupport.PeerZeroIndex;
    private const string KeyLoadPeerOneKey = "Benchmarks__KeyLoadEndpoints__" + ComparisonHostBindingsSupport.PeerOneIndex;
    private const string KeyLoadPeerTwoKey = "Benchmarks__KeyLoadEndpoints__" + ComparisonHostBindingsSupport.PeerTwoIndex;
    private const string QdrantEndpointKey = "Benchmarks__QdrantEndpoint";
    private const string QdrantApiKeySetting = "Benchmarks__QdrantApiKey";
    private const string Neo4jEndpointKey = "Benchmarks__Neo4jEndpoint";
    private const string Neo4jPasswordSetting = "Benchmarks__Neo4jPassword";
    private const string AdminKeySetting = "Benchmarks__AdminKey";
    private const string PostgresConnectionKey = "ConnectionStrings__benchmark-postgres";
    private const string RabbitConnectionKey = "ConnectionStrings__benchmark-rabbit";
    private const string RabbitManagementEndpointKey = "Benchmarks__RabbitManagementEndpoint";
    private const string RabbitUserKey = "Benchmarks__RabbitUser";
    private const string RabbitPasswordKey = "Benchmarks__RabbitPassword";
    private const string RedisConnectionKey = "ConnectionStrings__benchmark-redis";
    private const string PostgresImageKey = "Benchmarks__Images__Postgres";
    private const string QdrantImageKey = "Benchmarks__Images__Qdrant";
    private const string RabbitImageKey = "Benchmarks__Images__Rabbit";
    private const string RedisImageKey = "Benchmarks__Images__Redis";
    private const string Neo4jImageKey = "Benchmarks__Images__Neo4j";
    private const string OutputKey = "Benchmarks__Output";
    private const string StorageKey = "Benchmarks__Storage";
    private const string DocumentsKey = "Benchmarks__Documents";
    private const string OperationsKey = "Benchmarks__Operations";
    private const string WarmupKey = "Benchmarks__Warmup";
    private const string RepetitionsKey = "Benchmarks__Repetitions";
    private const string ConcurrencyKey = "Benchmarks__Concurrency";
    private const string PayloadBytesKey = "Benchmarks__PayloadBytes";
    private const string DimensionsKey = "Benchmarks__Dimensions";
    private const string TopKKey = "Benchmarks__TopK";
    private const string TimeoutSecondsKey = "Benchmarks__TimeoutSeconds";
    private const string GraphVerticesKey = "Benchmarks__GraphVertices";
    private const string GraphFanOutKey = "Benchmarks__GraphFanOut";
    private const string GraphDepthKey = "Benchmarks__GraphDepth";
    private const string OutputDirectoryPrefix = "keyload-comparison-cleanup-";
    private const string ReportFileName = "results.json";
    private const string SafeCleanupFailureCode = "ComparisonTargetCleanupFailed";
    private const string QdrantCleanupDiagnostic = "Qdrant cleanup failed: HttpRequestException";
    private const string FailedStatus = "failed";
    private const string SetupPrefix = "setup:";
    private const int ExpectedTargets = 6;
    private const int ExpectedCases = 60;
    private const string UnavailableEndpoint = "http://127.0.0.1:0";
    private const string AdminKey = "cleanup-admin-secret";
    private const string QdrantApiKey = "cleanup-qdrant-secret";
    private const string Neo4jPassword = "cleanup-neo4j-secret";
    private const string PostgresPassword = "cleanup-postgres-secret";
    private const string RabbitPassword = "cleanup-rabbit-secret";
    private const string RabbitUser = "cleanup-rabbit-user";
    private const string RedisPassword = "cleanup-redis-secret";

    [Test]
    public async Task AcHost007QdrantCleanupFailureFailsSafelyAfterZeroSampleReport()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), OutputDirectoryPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await ComparisonHostProcess.RunAsync([], Settings(outputDirectory),
                TestContext.Current!.Execution.CancellationToken);
            var reportText = await File.ReadAllTextAsync(Path.Combine(outputDirectory, ReportFileName));
            var report = JsonSerializer.Deserialize<ComparisonReport>(reportText, ReportWriter.JsonOptions)!;
            await AssertFailureBoundaryAsync(result, reportText, report);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static Dictionary<string, string> Settings(string outputDirectory) => new(StringComparer.OrdinalIgnoreCase)
    {
        [KeyLoadEndpointKey] = UnavailableEndpoint,
        [KeyLoadPeerZeroKey] = UnavailableEndpoint,
        [KeyLoadPeerOneKey] = "http://127.0.0.1:1",
        [KeyLoadPeerTwoKey] = "http://127.0.0.1:2",
        [QdrantEndpointKey] = UnavailableEndpoint,
        [QdrantApiKeySetting] = QdrantApiKey,
        [Neo4jEndpointKey] = UnavailableEndpoint,
        [Neo4jPasswordSetting] = Neo4jPassword,
        [AdminKeySetting] = AdminKey,
        [PostgresConnectionKey] = $"Host=127.0.0.1;Port=0;Username=benchmark;" +
            $"Password={PostgresPassword};Database=benchmark;Timeout=1",
        [RabbitConnectionKey] = $"amqp://benchmark:{RabbitPassword}@127.0.0.1:0/",
        [RabbitManagementEndpointKey] = UnavailableEndpoint,
        [RabbitUserKey] = RabbitUser,
        [RabbitPasswordKey] = RabbitPassword,
        [RedisConnectionKey] = $"127.0.0.1:0,password={RedisPassword}," +
            $"connectTimeout=250,syncTimeout=250,abortConnect=true",
        [PostgresImageKey] = "postgres-unavailable",
        [QdrantImageKey] = "qdrant-unavailable",
        [RabbitImageKey] = "rabbit-unavailable",
        [RedisImageKey] = "redis-unavailable",
        [Neo4jImageKey] = "neo4j-unavailable",
        [OutputKey] = outputDirectory,
        [StorageKey] = "temporary-process-test",
        [DocumentsKey] = "1",
        [OperationsKey] = "1",
        [WarmupKey] = "0",
        [RepetitionsKey] = "1",
        [ConcurrencyKey] = "1",
        [PayloadBytesKey] = "128",
        [DimensionsKey] = "2",
        [TopKKey] = "1",
        [TimeoutSecondsKey] = "1",
        [GraphVerticesKey] = "1",
        [GraphFanOutKey] = "1",
        [GraphDepthKey] = "1"
    };

    private static async Task AssertFailureBoundaryAsync(ComparisonHostExit result, string reportText,
        ComparisonReport report)
    {
        var processOutput = result.Stdout + result.Stderr + reportText;
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That((result.Stdout + result.Stderr).Contains(SafeCleanupFailureCode, StringComparison.Ordinal)).IsTrue();
        await Assert.That((result.Stdout + result.Stderr).Contains(QdrantCleanupDiagnostic, StringComparison.Ordinal)).IsTrue();
        await Assert.That(report.Targets.Length).IsEqualTo(ExpectedTargets);
        await Assert.That(report.Cases.Length).IsEqualTo(ExpectedCases);
        foreach (var item in report.Cases)
        {
            await Assert.That(item.Status).IsEqualTo(FailedStatus);
            await Assert.That(item.Detail).StartsWith(SetupPrefix);
            await Assert.That(item.Measurement).IsNull();
            await Assert.That(item.Samples.IsEmpty).IsTrue();
        }

        await AssertSecretsAbsentAsync(processOutput);
    }

    private static async Task AssertSecretsAbsentAsync(string output)
    {
        foreach (var secret in new[]
                 {
                     AdminKey, QdrantApiKey, Neo4jPassword, PostgresPassword, RabbitPassword, RabbitUser, RedisPassword
                 })
        {
            await Assert.That(output.Contains(secret, StringComparison.Ordinal)).IsFalse();
        }
    }
}
