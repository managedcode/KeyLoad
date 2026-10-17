using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlFetchCountRf3State(McpPersistedIdentity Writer, McpPersistedIdentity Reader,
    CommandRequest Seed, CommitReceipt Receipt)
{
    internal static readonly (string Id, string Json)[] Rows =
    [
        ("a", """{"label":"a"}"""),
        ("b", """{"label":"b"}"""),
        ("c", """{"label":"c"}""")
    ];
}
