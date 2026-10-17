using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal sealed record AdminDashboardScenario(PartitionRef Partition)
{
    internal const string Collection = "dashboard-documents";
    internal const string Queue = "dashboard-jobs";
    internal const string DocumentId = "document-1";
    internal const string MessageId = "message-1";
    internal const string Payload = "{\"title\":\"<script>window.dashboardInjection=true</script>\"}";
    internal const string PrivatePayload = "{\"private\":\"queue-payload-private\"}";
    private const string TenantPrefix = "dashboard-";
    private const string Database = "database";
    private const string Domain = "dashboard";
    private const string GuidFormat = "N";

    internal static async Task<AdminDashboardScenario> CreateAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(GuidFormat), Database, Domain,
            Guid.NewGuid().ToString(GuidFormat));
        var client = fixture.Client(McpCallerProtocol.Node1);
        await McpCallerAssertions.SdkSuccessAsync(await client.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(Collection, ResourceKind.Collection, Domain)), cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await client.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(Queue, ResourceKind.WorkQueue, Domain)), cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(new(Guid.NewGuid(), partition,
            [new PutDocument(Collection, DocumentId, Payload), new EnqueueMessage(Queue, MessageId, PrivatePayload)]), cancellationToken));
        return new(partition);
    }

    internal AdminResourcesRequest Resources => new(Partition.TenantId, Partition.DatabaseId);
    internal AdminQueueRequest Messages => new(new(Partition, Queue));
}
