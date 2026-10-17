namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

/// <summary>The owner-selected database groups, independent of production planner output.</summary>
internal static class WorkflowDatabaseGroups
{
    internal static readonly (string Key, string Name)[] Entries =
    [
        ("keyload", "KeyLoad"),
        ("postgresql", "PostgreSQL + pgvector"),
        ("qdrant", "Qdrant"),
        ("rabbitmq", "RabbitMQ"),
        ("redis", "Redis"),
        ("neo4j", "Neo4j"),
        ("mongodb", "MongoDB"),
        ("opensearch", "OpenSearch"),
        ("kurrentdb", "KurrentDB"),
        ("surrealdb", "SurrealDB"),
        ("helixdb", "HelixDB")
    ];
}
