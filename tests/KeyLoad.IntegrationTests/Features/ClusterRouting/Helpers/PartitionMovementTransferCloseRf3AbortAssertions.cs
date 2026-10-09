using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Fresh persisted-admin Abort must finish the real target-first/source-join chain.</summary>
internal static class PartitionMovementTransferCloseRf3AbortAssertions
{
    internal static Guid SourceClosureId(PartitionMoveRequest request)
        => PartitionMovementParentPhaseIds.For(request, PartitionMovementPublicParentRf3Administrator.PrincipalId,
            PartitionMovementParentPhaseRole.SourceClosure);

    internal static async Task<PartitionMoveResult> AbortAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(
            seed.FirstRequest with { Mode = PartitionMoveMode.Abort }, cancellationToken).ConfigureAwait(false));
        await Assert.That(actual.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
        await Assert.That(actual.Phase).IsEqualTo(PartitionMovePhase.Aborted);
        await SqlRf3Protocol.EqualAsync(seed.Partition, actual.Partition);
        await SqlRf3Protocol.EqualAsync(seed.Directory.ControlOwner, actual.SourceOwner);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
            new(PartitionMoveProtocol.Version, seed.Partition), cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement, placement);
        return actual;
    }
}
