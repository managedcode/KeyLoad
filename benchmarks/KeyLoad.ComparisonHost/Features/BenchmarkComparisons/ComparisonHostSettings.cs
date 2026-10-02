using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Validated settings required to execute the existing comparison workload.</summary>
/// <param name="Options">Validated workload options.</param>
/// <param name="RunId">Unique identifier used to isolate target data.</param>
/// <param name="KeyLoadEndpoint">KeyLoad endpoint.</param>
/// <param name="QdrantEndpoint">Qdrant endpoint.</param>
/// <param name="QdrantApiKey">Qdrant API key.</param>
/// <param name="Neo4jEndpoint">Neo4j endpoint.</param>
/// <param name="Neo4jPassword">Neo4j password.</param>
/// <param name="AdminKey">KeyLoad administrator key.</param>
/// <param name="PostgresConnection">PostgreSQL connection string.</param>
/// <param name="RabbitConnection">RabbitMQ connection string.</param>
/// <param name="RedisConnection">Redis connection string.</param>
/// <param name="PostgresImage">PostgreSQL image label.</param>
/// <param name="QdrantImage">Qdrant image label.</param>
/// <param name="RabbitImage">RabbitMQ image label.</param>
/// <param name="RedisImage">Redis image label.</param>
/// <param name="Neo4jImage">Neo4j image label.</param>
/// <param name="OutputDirectory">Absolute report output directory.</param>
/// <param name="Storage">Storage profile description.</param>
/// <param name="SourceRevision">Optional source revision.</param>
internal sealed record ComparisonHostSettings(
    ComparisonOptions Options,
    string RunId,
    Uri KeyLoadEndpoint,
    Uri QdrantEndpoint,
    string QdrantApiKey,
    Uri Neo4jEndpoint,
    string Neo4jPassword,
    string AdminKey,
    string PostgresConnection,
    string RabbitConnection,
    string RedisConnection,
    string PostgresImage,
    string QdrantImage,
    string RabbitImage,
    string RedisImage,
    string Neo4jImage,
    string OutputDirectory,
    string Storage,
    string? SourceRevision)
{
    /// <summary>Reads configuration using the established environment then command-line precedence.</summary>
    /// <param name="configuration">Comparison host configuration.</param>
    /// <returns>Validated settings, before client allocation.</returns>
    internal static ComparisonHostSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = ComparisonOptions.Read(configuration);
        var keyLoadEndpoint = ReadEndpoint(configuration, ComparisonHostConstants.KeyLoadEndpoint);
        var qdrantEndpoint = ReadEndpoint(configuration, ComparisonHostConstants.QdrantEndpoint);
        var qdrantApiKey = Required(configuration, ComparisonHostConstants.QdrantApiKey);
        var neo4jEndpoint = ReadEndpoint(configuration, ComparisonHostConstants.Neo4jEndpoint);
        var neo4jPassword = Required(configuration, ComparisonHostConstants.Neo4jPassword);
        var adminKey = Required(configuration, ComparisonHostConstants.AdminKey);
        var postgresConnection = Required(configuration, ComparisonHostConstants.PostgresConnection);
        var postgresImage = Required(configuration, ComparisonHostConstants.PostgresImage);
        var qdrantImage = Required(configuration, ComparisonHostConstants.QdrantImage);
        var rabbitConnection = Required(configuration, ComparisonHostConstants.RabbitConnection);
        var rabbitImage = Required(configuration, ComparisonHostConstants.RabbitImage);
        var redisConnection = Required(configuration, ComparisonHostConstants.RedisConnection);
        var redisImage = Required(configuration, ComparisonHostConstants.RedisImage);
        var neo4jImage = Required(configuration, ComparisonHostConstants.Neo4jImage);
        var storage = Required(configuration, ComparisonHostConstants.Storage);
        var output = Path.GetFullPath(Required(configuration, ComparisonHostConstants.Output));
        var sourceRevision = configuration[ComparisonHostConstants.SourceRevision];
        return new(options, Guid.NewGuid().ToString(ComparisonHostConstants.GuidFormat), keyLoadEndpoint,
            qdrantEndpoint, qdrantApiKey, neo4jEndpoint, neo4jPassword, adminKey, postgresConnection,
            rabbitConnection, redisConnection, postgresImage, qdrantImage, rabbitImage, redisImage, neo4jImage,
            output, storage, sourceRevision);
    }

    private static Uri ReadEndpoint(IConfiguration configuration, string key)
        => new(Required(configuration, key), UriKind.Absolute);

    private static string Required(IConfiguration configuration, string key)
        => configuration[key] ?? throw new InvalidOperationException(ComparisonHostConstants.MissingSettingPrefix + key);
}
