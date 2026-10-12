using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Issues a locally signed phase only after actual configured MAC and native peer admission.</summary>
internal static class ControlledPartitionMovementProcessIssuer
{
    private const string Missing = "The original admitted movement process phase is missing.";

    internal static async Task<ReplicatedOperation> IssueAsync(ControlledPartitionMovementNativeNode receiver,
        PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        ReplicatedOperation? operation = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var mac = admission.CreateControlMac();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var bytes = NativeSerialization.Serialize(original);
                var verified = await admission.VerifyAsync(bytes, mac.Sign(bytes, reply: false), cancellationToken);
                if (verified.Action != PartitionMovementTransportAction.Apply)
                { throw new InvalidOperationException(Missing); }
                var id = PartitionMovementControlPrincipal.Resolve(receiver.Database, verified.Envelope);
                var principal = receiver.Store.Read(view => receiver.Database.Principal(view, id,
                    receiver.Database.EvaluationClock.GetUtcNow()));
                GrainRequestAuthority.RequireAdministrator(principal);
                operation = receiver.Database.CreateVerifiedPartitionMovementOperation(verified.CommandId,
                    principal.Id, receiver.Database.EvaluationClock.GetUtcNow(), verified.Envelope);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return operation ?? throw new InvalidOperationException(Missing);
    }
}
