using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class StrongReadSessionSetup
{
    internal static async Task<StrongReadSessionState> RunAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers root, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, Domain);
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, Database, definition), token)));
        var owner = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, partition), token));
        await SqlRf3Protocol.EqualAsync(partition, owner.Partition);
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(Collection, SentinelId, SentinelJson)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Token.Position).IsGreaterThan(InvalidCut);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(owner.Incarnation, partition.AtomicPartitionId, receipt.Token.Position, owner.PlacementEpoch),
            [new(PutKind, Collection, SentinelId, CreatedRevision)], DurabilityProfile.QuorumProcessDurable), receipt);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection, Capability.DocumentsRead, token);
        var state = new StrongReadSessionState(partition, identity, [new(command, receipt)], outbox, null);
        await StrongReadSessionCurrent.CheckAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, StrongReadSessionState state,
        bool allowed, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allowed ? state.Identity.Principal.Grants : []
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
