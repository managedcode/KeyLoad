using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Only bounded unclaimed adjunct arms change; original active claim bytes remain untouched.</summary>
internal static class PartitionMovementActiveAdjunctRf3Fault
{
    internal static Guid[] Create(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        RequestCqrsProbeMarkerRecord primary, bool duplicate)
    {
        var first = CreateOne(wave, seed, primary, duplicate ? primary.RequestId : Guid.NewGuid());
        return duplicate ? [first, CreateOne(wave, seed, primary, primary.RequestId)] : [first];
    }

    private static Guid CreateOne(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        RequestCqrsProbeMarkerRecord primary, Guid sourceRequest)
        => wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            seed.FirstRequest.MoveId, null, RequestCqrsProbePhase.ParentReceiverIssueObserved,
            RequestCqrsProbeAction.Hold, sourceRequestId: sourceRequest, sourceArmId: primary.ArmId);

    internal static async Task RemoveAsync(TwoRf3MembershipWave wave, IEnumerable<Guid> arms,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var arm in arms)
        {
            var state = wave.QueryControls.ArmFor(arm);
            if (state.RequestId is not null)
            { throw new InvalidOperationException(PartitionMovementActiveAdjunctProtocol.MissingPrimary); }
            await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm,
                cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
