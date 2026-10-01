using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

internal static class BenchmarkResources
{
    // Multi-architecture manifest digests pin the actual content, including the PostgreSQL patch under pg18.
    private const string PostgresDigest = "sha256:2ba9ca5f2e7daa0f0e7723cba1ee9167bab54efd3640516a44ac1a928dd67e7a";
    private const string QdrantDigest = "sha256:94728574965d17c6485dd361aa3c0818b325b9016dac5ea6afec7b4b2700865f";
    private const string RabbitDigest = "sha256:aeee1db0ff9fdb1347f3585242c14ed10877b56c22eeaa113b21208ef3dd3edf";
    private const string RedisDigest = "sha256:c22af04bb576503bf16b3e34a1fd2fd82de0f765afd866d2e380145e0af30d78";

    public static void Add(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource>[] nodes,
        IResourceBuilder<ParameterResource> admin, string root)
    {
        string DataDirectory(string name)
        {
            var directory = Path.Combine(root, "external", name);
            Directory.CreateDirectory(directory);
            return directory;
        }
        var postgres = builder.AddPostgres("benchmark-postgres-server")
            .WithImage("pgvector/pgvector").WithImageTag("0.8.6-pg18").WithImageSHA256(PostgresDigest[7..])
            // The pgvector tag is not a PostgreSQL SemVer tag: explicitly select the PG18 data root.
            .WithBindMount(DataDirectory("postgres"), "/var/lib/postgresql")
            .WithArgs("-c", "fsync=on", "-c", "synchronous_commit=on");
        var database = postgres.AddDatabase("benchmark-postgres");
        var qdrantKey = builder.AddParameter("benchmark-qdrant-key", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), secret: true);
        var qdrant = builder.AddQdrant("benchmark-qdrant", qdrantKey).WithImageTag("v1.17.1").WithImageSHA256(QdrantDigest[7..])
            .WithDataBindMount(DataDirectory("qdrant"));
        var rabbit = builder.AddRabbitMQ("benchmark-rabbit").WithManagementPlugin()
            .WithImageTag("4.2.4-management").WithImageSHA256(RabbitDigest[7..]).WithDataBindMount(DataDirectory("rabbit"));
        var redis = builder.AddRedis("benchmark-redis").WithImageTag("8.4.0").WithImageSHA256(RedisDigest[7..])
            .WithDataBindMount(DataDirectory("redis"))
            .WithArgs("--appendonly", "yes", "--appendfsync", "always");
        var runner = builder.AddProject<Projects.KeyLoad_Comparisons>("comparisons", launchProfileName: null)
            .WithReference(database).WaitFor(database).WithReference(rabbit).WaitFor(rabbit).WithReference(redis).WaitFor(redis)
            .WithReference(qdrant).WaitFor(qdrant)
            .WithEnvironment("Benchmarks__KeyLoadEndpoint", nodes[0].GetEndpoint("http"))
            .WithEnvironment("Benchmarks__AdminKey", admin)
            .WithEnvironment("Benchmarks__QdrantEndpoint", qdrant.GetEndpoint("http"))
            .WithEnvironment("Benchmarks__QdrantApiKey", qdrantKey)
            .WithEnvironment("Benchmarks__Storage", "isolated host directories; containers use host bind mounts")
            .WithEnvironment("Benchmarks__Output", Path.GetFullPath(builder.Configuration["Benchmarks:Output"] ?? Path.Combine(root, "reports")))
            .WithEnvironment("Benchmarks__Images__Postgres", "docker.io/pgvector/pgvector:0.8.6-pg18@" + PostgresDigest)
            .WithEnvironment("Benchmarks__Images__Qdrant", "docker.io/qdrant/qdrant:v1.17.1@" + QdrantDigest)
            .WithEnvironment("Benchmarks__Images__Rabbit", "docker.io/library/rabbitmq:4.2.4-management@" + RabbitDigest)
            .WithEnvironment("Benchmarks__Images__Redis", "docker.io/library/redis:8.4.0@" + RedisDigest);
        foreach (var node in nodes) runner.WaitFor(node);
        foreach (var setting in new[] { "Seed", "Documents", "Operations", "Warmup", "Repetitions", "Concurrency", "PayloadBytes", "Dimensions", "TopK", "TimeoutSeconds", "SourceRevision" })
            if (builder.Configuration["Benchmarks:" + setting] is { } value) runner.WithEnvironment("Benchmarks__" + setting, value);
    }
}
