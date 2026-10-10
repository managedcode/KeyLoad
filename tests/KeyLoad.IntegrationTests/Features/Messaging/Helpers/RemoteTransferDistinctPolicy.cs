using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctPolicy
{
    internal static async Task RepairAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed parent,
        RemoteTransferColdSeed seed, QueueTransferInspection intent, CommandRequest accept,
        QueueTransferReceiptInspection proof, CancellationToken token)
    {
        var revoked = MessagingRf3Identity.WithCapability(seed.Identity.Principal, seed.Scenario.DestinationQueue,
            RemoteTransferDistinctProtocol.TargetCapabilities & ~Capability.QueuePublish);
        await ConfigureAsync(parent.Source, revoked, token);
        await RemoteTransferDistinctCallers.WithAsync(wave, seed, async (sdk, mcp) =>
        {
            await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(accept, token), ErrorCode.PermissionDenied);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, accept, token),
                ErrorCode.PermissionDenied, true);
            await AtomicProducerRf3Routes.RefusedAsync(sdk, mcp, accept, ErrorCode.PermissionDenied, token);
            await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
            await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed,
                RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);
        }, token);
        var restored = MessagingRf3Identity.WithCapability(revoked, seed.Scenario.DestinationQueue,
            RemoteTransferDistinctProtocol.TargetCapabilities);
        await ConfigureAsync(parent.Source, restored, token);
        await RemoteTransferDistinctCallers.WithAsync(wave, seed, async (sdk, mcp) =>
        { await RemoteTransferColdRefusals.OriginalEpochRefusedAsync(sdk, mcp, seed, token); }, token);
    }

    private static async Task ConfigureAsync(KeyLoadClient administrator, PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
}
