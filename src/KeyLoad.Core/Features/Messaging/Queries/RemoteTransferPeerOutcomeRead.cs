using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private StoredOutcome ReadRemoteTransferPeerOutcome(IKeyValueView view, PrincipalRecord principal,
        RemoteQueueTransferPeerCall call)
    {
        var partition = call.IntentClaims.Destination.Partition;
        var selection = CommandOutcomeKeyResolver.Select(view, principal.Id, call.OriginalCommandId,
            new(CommandOutcomeScopeKind.Partition, partition));
        var outcome = selection.Outcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        var placement = ReadPlacementWitness(view, partition);
        var command = new CommandRequest(call.OriginalCommandId, partition,
            ImmutableArray.Create<Mutation>(new AcceptQueueTransfer(call.IntentClaims.Destination, call.IntentToken)),
            placement.PlacementEpoch);
        var operation = new ReplicatedOperation(call.OriginalCommandId, OperationKind.Batch, principal.Id,
            Clock.GetUtcNow(), Identity<CommandRequest>(NativeSerialization.Serialize(command)));
        if (outcome.PolicyEpoch != principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
        if (outcome.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage); }
        if (outcome.Fingerprint != CommandFingerprint(operation))
        { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
        CommandOutcomeKeyResolver.ValidateSelectedScope(view, operation, selection);
        RequireRemoteTransferObservedStamp(outcome, call, principal);
        if (outcome.Result.Error is null)
        {
            _ = ReadRemoteTransferPeerReceipt(view, principal, call)
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        }
        return outcome;
    }

    private static void RequireRemoteTransferObservedStamp(StoredOutcome outcome,
        RemoteQueueTransferPeerCall call, PrincipalRecord principal)
    {
        var stamp = outcome.RemoteTransferAuthority;
        if (outcome.Result.Error is not null)
        {
            if (stamp is not null)
            { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid); }
            return;
        }
        if (stamp is null || stamp.LogicalPrincipalId != call.LogicalPrincipalId
            || stamp.TechnicalPrincipalId != principal.Id || stamp.TechnicalPolicyEpoch != outcome.PolicyEpoch
            || stamp.LogicalPolicyEpoch < RemoteTransferPeerProtocol.MinimumPolicyEpoch
            || !RemoteTransferDependencyShape.Digest(stamp.FieldHeaderDigest)
            || !RemoteTransferDependencyShape.Digest(stamp.ProofDigest)
            || !PhysicalOwnerEntryValidation.Same(stamp.SourceOwner, call.SourceOwner)
            || !PhysicalOwnerEntryValidation.Same(stamp.DestinationOwner, call.DestinationOwner))
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid); }
    }
}
