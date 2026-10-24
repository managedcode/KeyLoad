using KeyLoad.IntegrationTests.ClientApi;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record SqlNullOrderingClusterState(McpPersistedIdentity Writer, McpPersistedIdentity Reader,
    CommandRequest Seed, CommitReceipt Receipt, EntityRefLifetime Lifetime)
{
    internal EntityRef Stored(string id) => new(PostgresClusterState.Partition, SqlNullOrderingClusterProtocol.Collection, id)
    {
        Lifetime = Lifetime
    };

    internal static readonly (string Id, string Json)[] Rows =
    [
        ("missing", """{"label":"missing"}"""),
        ("null_a", """{"label":"null_a","score":null}"""),
        ("null_b", """{"label":"null_b","score":null}"""),
        ("low", """{"label":"low","score":1}"""),
        ("high", """{"label":"high","score":2}""")
    ];
}
