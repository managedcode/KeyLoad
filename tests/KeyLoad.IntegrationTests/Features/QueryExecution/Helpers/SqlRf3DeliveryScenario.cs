using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Independent persisted queue and document used by real SQL delivery and cancellation callers.</summary>
internal sealed record SqlRf3DeliveryScenario(PartitionRef Partition, long CommittedPosition)
{
    internal const string Queue = "jobs";
    internal const string Collection = "documents";
    internal const string MessageId = "message";
    internal const string DocumentId = "document";
    internal const string Payload = "{\"task\":\"process\"}";
    internal const string Headers = "{\"origin\":\"rf3\"}";
    internal const string SelectSql = "SELECT * FROM documents WHERE id = 'document'";
    internal const string PointPath = "point";
    internal const int FirstAttempt = 1;
    internal const int LeaseSeconds = 120;
    private const string TenantPrefix = "sql-delivery-";
    private const string Database = "agentdb";
    private const string Domain = "agent";

    internal QueueLaneRef Lane => new(Partition, Queue);
    internal InspectMessageRequest Inspect => new(Lane, MessageId);
    internal SqlOperationRequest Select => new(Partition, SelectSql);
    internal ReceiveRequest Receive(Guid requestId) => new(requestId, Lane, LeaseSeconds: LeaseSeconds);

    internal static async Task<SqlRf3DeliveryScenario> CreateAsync(KeyLoadClient sdk, CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        foreach (var (name, kind) in new[] { (Queue, ResourceKind.WorkQueue), (Collection, ResourceKind.Collection) })
        {
            await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(name, kind, partition.TransactionDomainId)), cancellationToken));
        }
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(new(Guid.NewGuid(), partition,
            [new EnqueueMessage(Queue, MessageId, Payload, Headers), new PutDocument(Collection, DocumentId, Payload)]), cancellationToken));
        return new(partition, receipt.Token.Position);
    }

    internal async Task VerifyReadAsync(QueryPage page)
    {
        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.Rows[0].EntityId).IsEqualTo(DocumentId);
        await Assert.That(page.Rows[0].Revision).IsEqualTo(McpCallerProtocol.FirstRevision);
        await Assert.That(page.Rows[0].Json).IsEqualTo(Payload);
        await Assert.That(page.Rows[0].Redacted).IsFalse();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(PointPath);
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(CommittedPosition);
    }
}
