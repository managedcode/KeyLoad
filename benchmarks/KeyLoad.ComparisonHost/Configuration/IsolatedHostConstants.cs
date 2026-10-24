namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Closed configuration and output tokens for the isolated execution host.</summary>
internal static class IsolatedHostConstants
{
    internal const string Image = "Benchmarks:Execution:Image";
    internal const string Connection = "Benchmarks:Execution:ConnectionString";
    internal const string Endpoints = "Benchmarks:Execution:Endpoints";
    internal const string ReplicaConnections = "Benchmarks:Execution:ReplicaConnections";
    internal const string User = "Benchmarks:Execution:User";
    internal const string Password = "Benchmarks:Execution:Password";
    internal const string ApiKey = "Benchmarks:Execution:ApiKey";
    internal const string JobId = "KEYLOAD_COMPARISON_JOB_ID";
    internal const string Failure = "IsolatedComparisonHostFailed";
    internal const string CleanupFailure = "IsolatedComparisonCleanupFailed";
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
}
