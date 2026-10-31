namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>The databases a benchmark job can deploy and the node counts each deployment supports.</summary>
internal static class BenchmarkDatabases
{
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
    internal const string Timescale = "TimescaleDB";
    internal const string SqlServer = "SQL Server";
    internal const string MySql = "MySQL";
    internal const string ClickHouse = "ClickHouse";
    internal const string InfluxDb = "InfluxDB";
    internal const int SingleNode = 1;
    internal const int Cluster = 3;

    private static readonly HashSet<string> Clustered = new(StringComparer.Ordinal)
    {
        KeyLoad, Postgres, Qdrant, Rabbit, Redis, Mongo, OpenSearch, Kurrent
    };

    private static readonly HashSet<string> Standalone = new(StringComparer.Ordinal)
    {
        Neo4j, SurrealDb, HelixDb, Timescale, SqlServer, MySql, ClickHouse, InfluxDb
    };

    internal static bool Supports(string database, int nodes)
        => nodes == SingleNode ? Clustered.Contains(database) || Standalone.Contains(database)
            : nodes == Cluster && Clustered.Contains(database);
}
