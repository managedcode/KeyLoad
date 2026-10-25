using KeyLoad.ClusterTests.ClientApi;
namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record SqlFetchCountClusterState(McpPersistedIdentity Writer, McpPersistedIdentity Reader,
    CommandRequest Seed, CommitReceipt Receipt, EntityRefLifetime Lifetime)
{
    internal static EntityRef Reference(string id) => new(PostgresClusterState.Partition, SqlFetchCountClusterProtocol.Collection, id);
    internal EntityRef Stored(string id) => Reference(id) with { Lifetime = Lifetime };

    internal static readonly (string Id, string Json)[] Rows =
    [
        ("a", """{"label":"a"}"""),
        ("b", """{"label":"b"}"""),
        ("c", """{"label":"c"}""")
    ];
}
