using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Hosting;

internal static class BenchmarkResources
{
    private const string ExternalDataDirectoryName = "external";
    private const string PostgresResourceName = "benchmark-postgres-server";
    private const string PostgresImageName = "pgvector/pgvector";
    private const string PostgresImageTag = "0.8.6-pg18";
    private const int DigestPrefixLength = 7;
    private const string PostgresDirectoryName = "postgres";
    private const string PostgresDataMount = "/var/lib/postgresql";
    private const string PostgresConfigurationArgument = "-c";
    private const string PostgresFsyncArgument = "fsync=on";
    private const string PostgresSynchronousCommitArgument = "synchronous_commit=on";
    private const string PostgresDatabaseName = "benchmark-postgres";
    private const string QdrantKeyParameterName = "benchmark-qdrant-key";
    private const int QdrantKeyEntropyBytes = 32;
    private const string QdrantResourceName = "benchmark-qdrant";
    private const string QdrantImageTag = "v1.17.1";
    private const string QdrantDirectoryName = "qdrant";
    private const string RabbitResourceName = "benchmark-rabbit";
    private const string RabbitImageTag = "4.2.4-management";
    private const string RabbitDirectoryName = "rabbit";
    private const string RedisResourceName = "benchmark-redis";
    private const string RedisImageTag = "8.4.0";
    private const string RedisDirectoryName = "redis";
    private const string RedisAppendOnlyArgument = "--appendonly";
    private const string RedisAppendOnlyValue = "yes";
    private const string RedisAppendFsyncArgument = "--appendfsync";
    private const string RedisAppendFsyncValue = "always";
    private const string Neo4jPasswordParameterName = "benchmark-neo4j-password";
    private const int Neo4jPasswordEntropyBytes = 24;
    private const string Neo4jResourceName = "benchmark-neo4j";
    private const string Neo4jImageAndDirectoryName = "neo4j";
    private const string Neo4jImageTag = "2026.09.0";
    private const string Neo4jDataMount = "/data";
    private const int Neo4jHttpPort = 7474;
    private const string HttpEndpointName = "http";
    private const string Neo4jAuthenticationEnvironment = "NEO4J_AUTH";
    private const string Neo4jAuthenticationPrefix = "neo4j/";
    private const string Neo4jInitialHeapEnvironment = "NEO4J_server_memory_heap_initial__size";
    private const string MegabyteUnit = "m";
    private const string Neo4jMaximumHeapEnvironment = "NEO4J_server_memory_heap_max__size";
    private const string Neo4jPageCacheEnvironment = "NEO4J_server_memory_pagecache_size";
    private const string RootHealthPath = "/";
    private const string ReportsDirectoryName = "reports";
    private const string KeyLoadEndpointEnvironment = "Benchmarks__KeyLoadEndpoint";
    private const int FirstNodeIndex = 0;
    private const string AdminKeyEnvironment = "Benchmarks__AdminKey";
    private const string QdrantEndpointEnvironment = "Benchmarks__QdrantEndpoint";
    private const string QdrantKeyEnvironment = "Benchmarks__QdrantApiKey";
    private const string Neo4jEndpointEnvironment = "Benchmarks__Neo4jEndpoint";
    private const string Neo4jPasswordEnvironment = "Benchmarks__Neo4jPassword";
    private const string StorageEnvironment = "Benchmarks__Storage";
    private const string StorageDescription = "isolated host directories; containers use host bind mounts";
    private const string PostgresImageEnvironment = "Benchmarks__Images__Postgres";
    private const string PostgresImageReferencePrefix = "docker.io/pgvector/pgvector:0.8.6-pg18@";
    private const string QdrantImageEnvironment = "Benchmarks__Images__Qdrant";
    private const string QdrantImageReferencePrefix = "docker.io/qdrant/qdrant:v1.17.1@";
    private const string RabbitImageEnvironment = "Benchmarks__Images__Rabbit";
    private const string RabbitImageReferencePrefix = "docker.io/library/rabbitmq:4.2.4-management@";
    private const string RedisImageEnvironment = "Benchmarks__Images__Redis";
    private const string RedisImageReferencePrefix = "docker.io/library/redis:8.4.0@";
    private const string Neo4jImageEnvironment = "Benchmarks__Images__Neo4j";
    private const string Neo4jImageReferencePrefix = "docker.io/library/neo4j:2026.09.0@";

    private const string EnvironmentPrefix = "Benchmarks__";
    // Multi-architecture manifest digests pin the actual content, including the PostgreSQL patch under pg18.
    internal const string PostgresDigest = "sha256:2ba9ca5f2e7daa0f0e7723cba1ee9167bab54efd3640516a44ac1a928dd67e7a";
    internal const string QdrantDigest = "sha256:94728574965d17c6485dd361aa3c0818b325b9016dac5ea6afec7b4b2700865f";
    internal const string RabbitDigest = "sha256:aeee1db0ff9fdb1347f3585242c14ed10877b56c22eeaa113b21208ef3dd3edf";
    internal const string RedisDigest = "sha256:c22af04bb576503bf16b3e34a1fd2fd82de0f765afd866d2e380145e0af30d78";
    internal const string Neo4jDigest = "sha256:91fb0bf237c41b7b3dcbe84703aa0b82e0d7d067b16e1c8ab21f03fc679edf4e";

    internal const string MongoDigest = "sha256:81a1c8842a09589fc8d5f285266f3340bf4abdf66700ba22988f14cc9b2b3118";
    internal const string OpenSearchDigest = "sha256:b5dd1512af2a99748c942cfbbd7f32162623336b210667d0fc6333c6321f171d";
    internal const string KurrentDigest = "sha256:ef49a58bab8bc4d7b08cd218f1bb85cf230b03edf5d136e98d6e86a3e5100b6a";
    // Official multi-platform Docker Hub index for SurrealDB v3.2.4.
    internal const string SurrealDbDigest = "sha256:51baed8709f57f67dcf04b30e3177db846803fa9342dae2be58c6fa5f8d59843";
    // Official GHCR linux/amd64 manifest for HelixDB server v0.0.10.
    internal const string HelixDbDigest = "sha256:1bd2b9f91fe63190a4cb2c7058e680eef635e9fa9df67a4fcfcc210d3fe1f68a";

    public static void Add(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource>[] nodes,
        IResourceBuilder<ParameterResource> admin, string root)
    {
        var options = AppHostOptionsRegistration.Get(builder);
        var deployment = options.Deployment.Value;
        string DataDirectory(string name)
        {
            var directory = Path.Combine(root, ExternalDataDirectoryName, name);
            Directory.CreateDirectory(directory);
            return directory;
        }
        var postgres = builder.AddPostgres(PostgresResourceName)
            .WithImage(PostgresImageName).WithImageTag(PostgresImageTag).WithImageSHA256(PostgresDigest[DigestPrefixLength..])
            // The pgvector tag is not a PostgreSQL SemVer tag: explicitly select the PG18 data root.
            .WithBindMount(DataDirectory(PostgresDirectoryName), PostgresDataMount)
            .WithArgs(PostgresConfigurationArgument, PostgresFsyncArgument, PostgresConfigurationArgument, PostgresSynchronousCommitArgument);
        var database = postgres.AddDatabase(PostgresDatabaseName);
        var qdrantKey = builder.AddParameter(QdrantKeyParameterName, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(QdrantKeyEntropyBytes)), secret: true);
        var qdrant = builder.AddQdrant(QdrantResourceName, qdrantKey).WithImageTag(QdrantImageTag).WithImageSHA256(QdrantDigest[DigestPrefixLength..])
            .WithDataBindMount(DataDirectory(QdrantDirectoryName));
        var rabbit = builder.AddRabbitMQ(RabbitResourceName).WithManagementPlugin()
            .WithImageTag(RabbitImageTag).WithImageSHA256(RabbitDigest[DigestPrefixLength..]).WithDataBindMount(DataDirectory(RabbitDirectoryName));
        var redis = builder.AddRedis(RedisResourceName).WithImageTag(RedisImageTag).WithImageSHA256(RedisDigest[DigestPrefixLength..])
            .WithDataBindMount(DataDirectory(RedisDirectoryName))
            .WithArgs(RedisAppendOnlyArgument, RedisAppendOnlyValue, RedisAppendFsyncArgument, RedisAppendFsyncValue);
        var neo4jPassword = builder.AddParameter(Neo4jPasswordParameterName, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(Neo4jPasswordEntropyBytes)), secret: true);
        var neo4j = builder.AddContainer(Neo4jResourceName, Neo4jImageAndDirectoryName, Neo4jImageTag).WithImageSHA256(Neo4jDigest[DigestPrefixLength..])
            .WithBindMount(DataDirectory(Neo4jImageAndDirectoryName), Neo4jDataMount).WithHttpEndpoint(targetPort: Neo4jHttpPort, name: HttpEndpointName)
            .WithEnvironment(Neo4jAuthenticationEnvironment, ReferenceExpression.Create($"{Neo4jAuthenticationPrefix}{neo4jPassword}"))
            .WithEnvironment(Neo4jInitialHeapEnvironment, deployment.Neo4jInitialHeapMegabytes.ToString(CultureInfo.InvariantCulture) + MegabyteUnit).WithEnvironment(Neo4jMaximumHeapEnvironment, deployment.Neo4jMaximumHeapMegabytes.ToString(CultureInfo.InvariantCulture) + MegabyteUnit)
            .WithEnvironment(Neo4jPageCacheEnvironment, deployment.Neo4jPageCacheMegabytes.ToString(CultureInfo.InvariantCulture) + MegabyteUnit).WithHttpHealthCheck(RootHealthPath);
        var runner = BenchmarkRunnerContainer.Create(builder,
                AppHostOptionsRegistration.Get(builder).Startup.Value.BenchmarkOutput ?? Path.Combine(root, ReportsDirectoryName))
            .WithReference(database).WaitFor(database).WithReference(rabbit).WaitFor(rabbit).WithReference(redis).WaitFor(redis)
            .WithReference(qdrant).WaitFor(qdrant)
            .WaitFor(neo4j)
            .WithEnvironment(KeyLoadEndpointEnvironment, nodes[FirstNodeIndex].GetEndpoint(HttpEndpointName))
            .WithEnvironment(AdminKeyEnvironment, admin)
            .WithEnvironment(QdrantEndpointEnvironment, qdrant.GetEndpoint(HttpEndpointName))
            .WithEnvironment(QdrantKeyEnvironment, qdrantKey)
            .WithEnvironment(Neo4jEndpointEnvironment, neo4j.GetEndpoint(HttpEndpointName))
            .WithEnvironment(Neo4jPasswordEnvironment, neo4jPassword)
            .WithEnvironment(StorageEnvironment, StorageDescription)
            .WithEnvironment(PostgresImageEnvironment, PostgresImageReferencePrefix + PostgresDigest)
            .WithEnvironment(QdrantImageEnvironment, QdrantImageReferencePrefix + QdrantDigest)
            .WithEnvironment(RabbitImageEnvironment, RabbitImageReferencePrefix + RabbitDigest)
            .WithEnvironment(RedisImageEnvironment, RedisImageReferencePrefix + RedisDigest)
            .WithEnvironment(Neo4jImageEnvironment, Neo4jImageReferencePrefix + Neo4jDigest);
        BenchmarkCallerBindings.Apply(runner, nodes, rabbit);
        ConfigureRunner(builder, runner, nodes);
    }

    private static void ConfigureRunner(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> runner,
        IResourceBuilder<ContainerResource>[] nodes)
    {
        foreach (var node in nodes)
        {
            runner.WaitFor(node);
        }
        var options = AppHostOptionsRegistration.Get(builder);
        var workload = options.BenchmarkWorkload!.Value;
        foreach (var property in typeof(KeyLoad.Comparisons.ComparisonOptions).GetProperties().Where(property => property.CanRead))
        {
            runner.WithEnvironment(EnvironmentPrefix + property.Name, Convert.ToString(property.GetValue(workload), CultureInfo.InvariantCulture));
        }
        runner.WithEnvironment(EnvironmentPrefix + Profile, options.Startup.Value.BenchmarkProfile);
        if (options.BenchmarkRelay.Value.EvidenceProfile is { } evidenceProfile)
        { runner.WithEnvironment(EnvironmentPrefix + nameof(BenchmarkRelayOptions.EvidenceProfile), evidenceProfile); }
        if (options.BenchmarkRelay.Value.SourceRevision is { } sourceRevision)
        { runner.WithEnvironment(EnvironmentPrefix + nameof(BenchmarkRelayOptions.SourceRevision), sourceRevision); }
    }
    private const string Profile = "Profile";
}
