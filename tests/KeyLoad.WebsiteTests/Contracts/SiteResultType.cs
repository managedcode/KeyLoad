namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>One data type of the benchmark result contract: its case ids and the databases compared with KeyLoad.</summary>
internal sealed record SiteResultType(string Id, string Label, string[] Cases, string[] Databases)
{
    // The site's declared inventory, stated here independently of the JavaScript contract; KeyLoad leads every type.
    internal static readonly SiteResultType[] All =
    [
        new("documents", "Documents", ["load", "point-read", "range-query", "update", "delete", "mixed"],
            [SiteTokens.KeyLoad, "MongoDB", "SurrealDB"]),
        new("key-value", "Key-value", ["load", "get", "set", "delete", "mixed"], [SiteTokens.KeyLoad, "Redis"]),
        new("relational", "Relational", ["load", "pk-select", "range-select", "update", "delete", "mixed"],
            [SiteTokens.KeyLoad, "PostgreSQL + pgvector", "SQL Server", "MySQL"]),
        new("graph", "Graph", ["load", "neighbors", "traverse"], [SiteTokens.KeyLoad, "Neo4j", "HelixDB", "SurrealDB"]),
        new("vector", "Vector", ["load", "exact-top10"], [SiteTokens.KeyLoad, "Qdrant", "PostgreSQL + pgvector"]),
        new("search", "Search", ["load", "index", "term-query", "and-query"], [SiteTokens.KeyLoad, "OpenSearch"]),
        new("time-series", "Time series", ["load", "range-read", "aggregate", "windows"],
            [SiteTokens.KeyLoad, "TimescaleDB", "InfluxDB"]),
        new("analytics", "Analytics", ["load", "group-by", "filtered-aggregate"], [SiteTokens.KeyLoad, "ClickHouse"]),
        new("queue", "Queue", ["cycle"], [SiteTokens.KeyLoad, "RabbitMQ"]),
        new("events", "Events", ["load", "append", "read", "mixed"], [SiteTokens.KeyLoad, "KurrentDB"]),
    ];

    // A case measured once compares its seconds instead of operations per second.
    internal static readonly string[] TimedCases = ["index"];

    internal static SiteResultType Find(string id) => All.Single(type => type.Id == id);
}
