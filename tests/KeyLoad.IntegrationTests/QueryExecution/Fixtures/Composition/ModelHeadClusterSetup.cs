using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadClusterSetup
{
    internal static async Task<ModelHeadClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef("model-head-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        foreach (var (name, kind) in new[] { (ModelHeadClusterState.Events, ResourceKind.StreamSet), (ModelHeadClusterState.Queue, ResourceKind.WorkQueue) })
        {
            var resource = new ResourceDefinition(name, kind, partition.TransactionDomainId)
            {
                FieldPolicies = [new("/secret", "private", "private.read", "private.use")],
                HeaderPolicies = [new("/privateHeader", "private", "private.read", "private.use")]
            };
            await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), token)));
        }
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new AppendEvents(ModelHeadClusterState.Events, ModelHeadClusterState.Stream,
                [new("created", "Created", ModelHeadClusterState.Payload("first"), ModelHeadClusterState.Headers),
                 new("paid", "Paid", ModelHeadClusterState.Payload("second"), ModelHeadClusterState.Headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(ModelHeadClusterState.Queue, "ready", ModelHeadClusterState.Payload("queue"), ModelHeadClusterState.Headers, ModelHeadClusterState.NotBefore)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(receipt.Token.Incarnation, partition.AtomicPartitionId, receipt.Token.Position, receipt.Token.OwnershipEpoch),
            [new("appendEvents", ModelHeadClusterState.Events, ModelHeadClusterState.Stream, 2L), new("enqueue", ModelHeadClusterState.Queue, "ready", 1L)],
            DurabilityProfile.QuorumProcessDurable), receipt);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, ModelHeadClusterState.Events, Capability.Query, token);
        var source = new StreamRef(partition, ModelHeadClusterState.Events, ModelHeadClusterState.Stream, 1L);
        var original = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(new(source, Limit: 10), token));
        await ModelHeadClusterCurrent.SourceAsync(original, source);
        var state = new ModelHeadClusterState(partition, command, receipt, identity,
            original with
            {
                CutPosition = 0L,
                SnapshotCutPosition = 0L
            },
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token));
        await PolicyAsync(root, state, false, 2L, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, ModelHeadClusterState state, bool read, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(state.Partition.DatabaseId, ModelHeadClusterState.Events, Capability.Query | (read ? Capability.EventsRead : Capability.None)),
                new(state.Partition.DatabaseId, ModelHeadClusterState.Queue, Capability.Query | (read ? Capability.QueueInspect : Capability.None))]
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
