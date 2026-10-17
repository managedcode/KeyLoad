using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
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
        if (original.Envelope.Stage == PartitionMovePeerStage.ControlApplyCommand)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority);
        }
        if (ControlledPartitionMovementProcessScope.Current is { } process)
        {
            return await process.SubmitAsync(receiver, runtime, original, cancellationToken);
        }
        OperationResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            {
                using var mac = admission.CreateControlMac();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var bytes = NativeSerialization.Serialize(original);
                    var verified = await admission.VerifyAsync(bytes, mac.Sign(bytes, reply: false), cancellationToken);
                    if (verified.Action != PartitionMovementTransportAction.Apply)
                    {
                        throw new InvalidOperationException("Only an original admitted logged phase may be submitted here.");
                    }
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
                    await PartitionMovementNativeIdentityAssertions.VerifyAsync(operation, receiver.Database.Limits);
                    result = receiver.Journal.Submit(operation, cancellationToken);
                }, failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The original verified native phase has no result.");
    }

    internal static async Task<OperationResult> SubmitAsync(ControlledPartitionMovementNode receiver,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest original, PartitionMoveLifetimeProofV1 proof, CancellationToken cancellationToken)
    {
        if (ControlledPartitionMovementProcessScope.Current is { } process)
        {
            return await process.SubmitAsync(receiver, runtime, original, proof, cancellationToken);
        }
        if (original.Envelope.Stage != PartitionMovePeerStage.ControlApplyCommand)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority);
        }
        OperationResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var mac = admission.CreateControlMac();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var body = NativeSerialization.Serialize(PartitionControlLifetimeTransportV2.Create(original, proof));
                var verified = await admission.VerifyControlledLifetimeAsync(body, mac.SignControlledLifetime(body), cancellationToken);
                var request = verified.Request;
                if (request.Action != PartitionMovementTransportAction.Apply)
                {
                    throw new InvalidOperationException("Only an original admitted controlled effect may be submitted here.");
                }
                var principalId = PartitionMovementControlPrincipal.Resolve(receiver.Database, request.Envelope);
                var principal = receiver.Store.Read(view => receiver.Database.Principal(view, principalId,
                    receiver.Database.EvaluationClock.GetUtcNow()));
                GrainRequestAuthority.RequireAdministrator(principal);
                var operation = receiver.Database.CreateVerifiedPartitionMovementOperation(request.CommandId,
                    principal.Id, receiver.Database.EvaluationClock.GetUtcNow(), request.Envelope, verified.Proof);
                await PartitionMovementNativeIdentityAssertions.VerifyAsync(operation, receiver.Database.Limits);
                result = receiver.Journal.Submit(operation, cancellationToken);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The original verified controlled effect has no result.");
    }
}
