using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains the original negative peer and unconfigured-owner checks with disposable current-engine admission.</summary>
internal static class ControlledPartitionMovementPrepareAdmission
{
    internal static async Task<DatabaseEngine> RejectAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, PartitionMovementTransportRequest request, CancellationToken token)
    {
        if (ControlledPartitionMovementProcessScope.Current is null)
        { return await RejectOriginalAsync(source, target, runtime, admission, request, token); }
        DatabaseEngine? original = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fresh = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership,
                runtime.PartitionMovement, source.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(async () =>
                original = await RejectOriginalAsync(source, target, runtime, fresh, request, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return original ?? throw new InvalidOperationException("The original negative ownership evidence is absent.");
    }

    private static async Task<DatabaseEngine> RejectOriginalAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, PartitionMovementTransportRequest request, CancellationToken token)
    {
        await ControlledPartitionMovementAdmissionAssertions.RejectAsync(source, target, admission, request, token);
        return await ControlledMovementOwnerAdmissionAssertions.RejectAsync(source, runtime, admission, request, token);
    }
}
