using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Preserves primary and disposal failures while verifying each phase against its current native owner.</summary>
internal static class ControlledPartitionMovementFreshAdmission
{
    internal static async Task<PartitionMovementTransportRequest> VerifyAsync(ControlledPartitionMovementNode node,
        ServerRuntimeOptions runtime, PartitionMovementTransportRequest original, CancellationToken token)
    {
        PartitionMovementTransportRequest? verified = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(runtime.Node, node.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership,
                runtime.PartitionMovement, node.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var mac = admission.CreateControlMac();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var bytes = NativeSerialization.Serialize(original);
                    verified = await admission.VerifyAsync(bytes, mac.Sign(bytes, reply: false), token);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return verified ?? throw new InvalidOperationException("The original process phase was not admitted.");
    }

    internal static async Task<(PartitionMovementTransportRequest Request, PartitionMoveLifetimeProofV1 Proof)>
        VerifyControlledLifetimeAsync(ControlledPartitionMovementNode node, ServerRuntimeOptions runtime,
            PartitionMovementTransportRequest original, PartitionMoveLifetimeProofV1 proof, CancellationToken token)
    {
        (PartitionMovementTransportRequest Request, PartitionMoveLifetimeProofV1 Proof)? verified = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(runtime.Node, node.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership,
                runtime.PartitionMovement, node.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var mac = admission.CreateControlMac();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var body = NativeSerialization.Serialize(PartitionControlLifetimeTransportV2.Create(original, proof));
                    verified = await admission.VerifyControlledLifetimeAsync(body, mac.SignControlledLifetime(body), token);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return verified ?? throw new InvalidOperationException("The original process controlled effect was not admitted.");
    }
}
