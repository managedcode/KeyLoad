using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses actual configured MAC and DNS-pinned native admission before any private phase issuance.</summary>
internal static class ControlledPartitionMovementVerifiedSubmit
{
    internal static async Task<OperationResult> SubmitAsync(ControlledPartitionMovementNode receiver,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        if (ControlledPartitionMovementProcessScope.Current is { } process)
        { return await process.SubmitAsync(receiver, runtime, original, cancellationToken); }
        OperationResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var key = Convert.FromBase64String(runtime.Node.Value.MembershipAuthority.Mode
                == MembershipAuthoritySettingsProtocol.Authority ? runtime.Node.Value.PeerSecret
                : runtime.Node.Value.MembershipAuthority.AuthorityPeerSecret!);
            try
            {
                using var mac = new PartitionMovementMac(key, receiver.Database.Limits.MaxBatchBytes);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var bytes = NativeSerialization.Serialize(original);
                    var verified = await admission.VerifyAsync(bytes, mac.Sign(bytes, reply: false), cancellationToken);
                    if (verified.Action != PartitionMovementTransportAction.Apply)
                    { throw new InvalidOperationException("Only an original admitted logged phase may be submitted here."); }
                    var principalId = PartitionMovementControlPrincipal.Resolve(receiver.Database, verified.Envelope);
                    var principal = receiver.Store.Read(view => receiver.Database.Principal(view, principalId,
                        receiver.Database.EvaluationClock.GetUtcNow()));
                    GrainRequestAuthority.RequireAdministrator(principal);
                    var operation = verified.Envelope.Stage == PartitionMovePeerStage.ControlCheckpoint
                        ? receiver.Database.CreateVerifiedPartitionMovementCheckpointOperation(verified.CommandId,
                            principal.Id, receiver.Database.EvaluationClock.GetUtcNow(), verified.Envelope,
                            new ReadExecutionBudget(runtime.Core.DatabaseLimits, receiver.Database.EvaluationClock, cancellationToken))
                        : verified.Envelope.Grant is { RequireReceiverIssuance: true }
                            ? receiver.Database.CreateVerifiedPartitionMovementParentEffect(verified.CommandId,
                                principal.Id, verified.Envelope, new ReadExecutionBudget(runtime.Core.DatabaseLimits,
                                    receiver.Database.EvaluationClock, cancellationToken))
                            : receiver.Database.CreateVerifiedPartitionMovementOperation(verified.CommandId,
                                principal.Id, receiver.Database.EvaluationClock.GetUtcNow(), verified.Envelope);
                    result = receiver.Journal.Submit(operation, cancellationToken);
                }, failures);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The original verified native phase has no result.");
    }
}
