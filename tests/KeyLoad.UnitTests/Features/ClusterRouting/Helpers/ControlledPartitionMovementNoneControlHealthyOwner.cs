using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Rebinds admission to the actual cold-reopened database before original replay and further healthy native effects.</summary>
internal static class ControlledPartitionMovementNoneControlHealthyOwner
{
    private const int FirstInstallPage = 0;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementTransportRequest originalRequest,
        PartitionMovePhaseResult originalCaptured, Func<PartitionMovementPeerAdmission, Task> continuation,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership,
                runtime.PartitionMovement, source.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var healthy = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime,
                    admission, originalRequest with { Envelope = originalRequest.Envelope with { Nonce = Guid.NewGuid() } },
                    cancellationToken);
                await Assert.That(healthy.Error).IsNull();
                var grant = healthy.Get<PartitionMovePhaseResult>().Grant
                    ?? throw new InvalidOperationException("The repaired original authorization is absent.");
                await Assert.That(grant.GrantId).IsEqualTo(originalRequest.CommandId);
                await Assert.That(grant.PhaseCommandId).IsEqualTo(
                    ControlledPartitionMovementTargetPhaseIds.InstallCommand(FirstInstallPage));
                await Assert.That(JsonDefaults.Serialize(healthy.Get<PartitionMovePhaseResult>().Control)
                    .SequenceEqual(JsonDefaults.Serialize(originalCaptured.Control))).IsTrue();
                await ControlledPartitionMovementNoneControlReplay.AssertAsync(source, runtime, admission,
                    originalRequest, healthy, cancellationToken);
                await continuation(admission);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
