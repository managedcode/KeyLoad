using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantDedicatedOwnerRf3Assertions
{
    private const long OriginalDocumentRevision = 1;
    private const long NoOutstandingGrants = 0;
    private const long PlacementRevisionIncrement = 1;

    internal static async Task SiblingAsync(TwoRf3MembershipWave wave, TenantDedicatedOwnerRf3State state,
        long directoryRevision, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            wave.Profile.AdminKey, async root =>
            {
                await SqlRf3Protocol.EqualAsync(state.SiblingPlacement with
                {
                    DirectoryRevision = directoryRevision
                },
                    await TenantPackingActivationRf3Admission.ReadAsync(root, state.Sibling.Partition, token));
                return state.SiblingPlacement;
            }, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            state.Sibling.Secret, async callers =>
            {
                await TenantPackingActivationRf3Assertions.ReplayAsync(callers, state.SiblingSeed, state.SiblingReceipt, token);
                await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(callers, state.Sibling,
                    RequestCqrsRf3Protocol.DocumentJson, OriginalDocumentRevision, token);
                return state.SiblingReceipt;
            }, token);
    }

    internal static async Task DeniedAsync(TwoRf3MembershipWave wave, TenantDedicatedOwnerRf3State state,
        CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            state.Sibling.Secret, async callers =>
            {
                var sdk = await callers.Sdk.MovePartitionAsync(state.DeniedMove, token);
                await Assert.That(sdk.IsSuccess).IsFalse();
                await Assert.That(sdk.Value).IsNull();
                await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
                await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(PartitionMovePublicProtocol.ToolName,
                    state.DeniedMove, token), ErrorCode.PermissionDenied, dispatched: true);
                return state.SiblingReceipt;
            }, token);
    }

    internal static async Task NoSiblingParentAsync(TwoRf3MembershipWave wave,
        TenantDedicatedOwnerRf3State state)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var original = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node, state.DeniedMove, Guid.Empty);
            await Assert.That(original.Header).IsNull();
            await Assert.That(original.Pending).IsNull();
            await Assert.That(original.LastIssued).IsNull();
            await Assert.That(original.OutstandingMoveGrants).IsEqualTo(NoOutstandingGrants);
            await Assert.That(original.OutstandingDatabaseGrants).IsEqualTo(NoOutstandingGrants);
        }
    }

    internal static async Task DedicatedAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantDedicatedOwnerRf3State state, PartitionMoveResult moved, long directoryRevision, CancellationToken token)
    {
        var rowRevision = checked(state.Original.Revision + PlacementRevisionIncrement);
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seed, state.Move, state.Original,
            seed.Directory.ControlOwner, moved, rowRevision, directoryRevision, token);
        await Assert.That(moved.DestinationOwner.PhysicalShardId).IsNotEqualTo(state.SiblingPlacement.PhysicalShardId);
        await Assert.That(moved.DestinationOwner.Incarnation).IsNotEqualTo(state.SiblingPlacement.Incarnation);
        await Assert.That(moved.DestinationOwner.VoterIds.Intersect(state.SiblingPlacement.VoterIds).Any()).IsFalse();
    }
}
