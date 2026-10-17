using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadRf3Setup
{
    internal static async Task<ModelHeadRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root, CancellationToken token)
    {
        var partition = new PartitionRef("model-head-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        foreach (var (name, kind) in new[] { (ModelHeadRf3State.Events, ResourceKind.StreamSet), (ModelHeadRf3State.Queue, ResourceKind.WorkQueue) })
        {
            var resource = new ResourceDefinition(name, kind, partition.TransactionDomainId)
            {
                FieldPolicies = [new("/secret", "private", "private.read", "private.use")],
                HeaderPolicies = [new("/privateHeader", "private", "private.read", "private.use")]
            };
            await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), token)));
        }
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new AppendEvents(ModelHeadRf3State.Events, ModelHeadRf3State.Stream,
                [new("created", "Created", ModelHeadRf3State.Payload("first"), ModelHeadRf3State.Headers),
                 new("paid", "Paid", ModelHeadRf3State.Payload("second"), ModelHeadRf3State.Headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(ModelHeadRf3State.Queue, "ready", ModelHeadRf3State.Payload("queue"), ModelHeadRf3State.Headers, ModelHeadRf3State.NotBefore)]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        await SqlRf3Protocol.EqualAsync(partition, placement.Partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(placement.Incarnation, partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [new("appendEvents", ModelHeadRf3State.Events, ModelHeadRf3State.Stream, 2L), new("enqueue", ModelHeadRf3State.Queue, "ready", 1L)],
            DurabilityProfile.QuorumProcessDurable), receipt);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, ModelHeadRf3State.Events, Capability.Query, token);
        var source = new StreamRef(partition, ModelHeadRf3State.Events, ModelHeadRf3State.Stream, 1L);
        var original = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(new(source, Limit: 10), token));
        await ModelHeadRf3Current.SourceAsync(original, source);
        var state = new ModelHeadRf3State(partition, command, receipt, identity,
            original with
            {
                CutPosition = 0L,
                SnapshotCutPosition = 0L
            },
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token));
        await PolicyAsync(root, state, false, 2L, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, ModelHeadRf3State state, bool read, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(state.Partition.DatabaseId, ModelHeadRf3State.Events, Capability.Query | (read ? Capability.EventsRead : Capability.None)),
                new(state.Partition.DatabaseId, ModelHeadRf3State.Queue, Capability.Query | (read ? Capability.QueueInspect : Capability.None))]
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
