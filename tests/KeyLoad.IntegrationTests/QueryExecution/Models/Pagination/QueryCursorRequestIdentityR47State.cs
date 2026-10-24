
namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record QueryCursorRequestIdentityR47State(string Credential,
    PartitionRef Partition, QueryRequest OriginalRequest, QueryRequest ChangedRequest,
    QueryRequest CompleteRequest, QueryRow[] CompleteRows, DocumentResult[] Documents,
    AdminResourceInfo[] Definitions, string AccessPath, CommandRequest[] Commands,
    CommitReceipt[] Receipts, OutboxStatus Outbox, QueryPage[] OriginalPages)
{
    public override string ToString() => nameof(QueryCursorRequestIdentityR47State);
}
