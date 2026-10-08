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
                var key = Convert.FromBase64String(runtime.Node.Value.MembershipAuthority.Mode
                    == MembershipAuthoritySettingsProtocol.Authority ? runtime.Node.Value.PeerSecret
                    : runtime.Node.Value.MembershipAuthority.AuthorityPeerSecret!);
                try
                {
                    using var mac = new PartitionMovementMac(key, node.Database.Limits.MaxBatchBytes);
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        var bytes = NativeSerialization.Serialize(original);
                        verified = await admission.VerifyAsync(bytes, mac.Sign(bytes, reply: false), token);
                    }, failures);
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return verified ?? throw new InvalidOperationException("The original process phase was not admitted.");
    }
}
