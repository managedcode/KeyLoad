using KeyLoad.IntegrationTests.ClientApi;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record SqlFetchCountClusterState(McpPersistedIdentity Writer, McpPersistedIdentity Reader,
    CommandRequest Seed, CommitReceipt Receipt)
{
    internal static readonly (string Id, string Json)[] Rows =
    [
        ("a", """{"label":"a"}"""),
        ("b", """{"label":"b"}"""),
        ("c", """{"label":"c"}""")
    ];
}
