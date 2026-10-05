namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Closed configuration and output tokens for the isolated native host.</summary>
internal static class IsolatedHostConstants
{
    internal const string Image = "Benchmarks:Native:Image";
    internal const string Connection = "Benchmarks:Native:ConnectionString";
    internal const string Endpoints = "Benchmarks:Native:Endpoints";
    internal const string ReplicaConnections = "Benchmarks:Native:ReplicaConnections";
    internal const string User = "Benchmarks:Native:User";
    internal const string Password = "Benchmarks:Native:Password";
    internal const string ApiKey = "Benchmarks:Native:ApiKey";
    internal const string JobId = "KEYLOAD_COMPARISON_JOB_ID";
    internal const string Failure = "IsolatedComparisonHostFailed";
    internal const string Cancelled = "IsolatedComparisonHostCancelled";
    internal const string CleanupFailure = "IsolatedComparisonCleanupFailed";
    internal const string WorkerFile = "worker.json";
    internal const string PendingPrefix = ".worker-";
    internal const string PendingSuffix = ".tmp";
    internal const string Measured = "measured";
    internal const string UnsupportedTopology = "unsupportedTopology";
    internal const string KeyLoad = "KeyLoad";
    internal const string Postgres = "PostgreSQL + pgvector";
    internal const string Qdrant = "Qdrant";
    internal const string Rabbit = "RabbitMQ";
    internal const string Redis = "Redis";
    internal const string Neo4j = "Neo4j";
    internal const string Mongo = "MongoDB";
    internal const string OpenSearch = "OpenSearch";
    internal const string Kurrent = "KurrentDB";
    internal const string SurrealDb = "SurrealDB";
    internal const string HelixDb = "HelixDB";
    internal const string Http = "http";
    internal const string Https = "https";
    internal const string RootPath = "/";
    internal const string Basic = "Basic";
    internal const string CredentialSeparator = ":";
    internal const int FileBufferBytes = 65_536;
    internal const int CleanupTimeoutSeconds = 30;
}
