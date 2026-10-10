using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Owns unique real work queues in two actual RF3 atomic partitions.</summary>
internal sealed record MessagingRf3Scenario(PartitionRef SourcePartition, PartitionRef DestinationPartition)
{
    internal const string SourceQueueName = "source-jobs";
    internal const string DestinationQueueName = "destination-jobs";
    internal const string TimeoutQueueName = "timeouts";
    internal const string SecretField = "/secret";
    internal const string SensitiveGrant = "messaging.secret";
    internal const string WriteGrant = "messaging.secret.write";
    internal const string ProtectedPayload = "{\"secret\":\"message-payload-canary\"}";
    internal const string ProtectedHeaders = "{\"secret\":\"message-header-canary\"}";
    internal const string SagaTimeoutPayload = "{\"secret\":\"saga-timeout-canary\"}";
    private const string TenantPrefix = "messaging-rf3-";
    private const string Database = "agentdb";
    private const string Domain = "messaging";

    internal QueueLaneRef SourceQueue => new(SourcePartition, SourceQueueName);
    internal QueueLaneRef DestinationQueue => new(DestinationPartition, DestinationQueueName);
    internal QueueLaneRef TimeoutQueue => new(SourcePartition, TimeoutQueueName);

    internal static Task<MessagingRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
        => CreateAsync(fixture, null, cancellationToken);

    internal static async Task<MessagingRf3Scenario> CreateAsync(ClusterFixture fixture,
        QueuePolicy? destinationPolicy, CancellationToken cancellationToken)
    {
        var tenant = TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var source = new PartitionRef(tenant, Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var destination = new PartitionRef(tenant, Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureQueueAsync(administrator, new(source, SourceQueueName), null, cancellationToken);
        await ConfigureQueueAsync(administrator, new(source, TimeoutQueueName), null, cancellationToken);
        await ConfigureQueueAsync(administrator, new(destination, DestinationQueueName), destinationPolicy, cancellationToken);
        return new(source, destination);
    }

    private static Task<ResourceDefinition> ConfigureQueueAsync(KeyLoadClient administrator, QueueLaneRef lane,
        QueuePolicy? queuePolicy, CancellationToken cancellationToken)
    {
        var policy = new SensitiveFieldPolicy(SecretField, "messaging-private",
            RawReadGrant: string.Concat(SensitiveGrant, ".read"),
            RawUseGrant: string.Concat(SensitiveGrant, ".use"),
            WriteGrant: string.Concat(SensitiveGrant, ".write"));
        var resource = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, lane.Partition.TransactionDomainId)
        {
            FieldPolicies = [policy],
            HeaderPolicies = [policy]
        };
        if (queuePolicy is not null)
        { resource = resource with { QueuePolicy = queuePolicy }; }
        var request = new ConfigureResourceRequest(lane.Partition.TenantId, lane.Partition.DatabaseId, resource);
        return CompleteConfigurationAsync(administrator, request, cancellationToken);
    }

    private static async Task<ResourceDefinition> CompleteConfigurationAsync(KeyLoadClient administrator,
        ConfigureResourceRequest request, CancellationToken cancellationToken)
        => await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(), request,
            cancellationToken));
}
