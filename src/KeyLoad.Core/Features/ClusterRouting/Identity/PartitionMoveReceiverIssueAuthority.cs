using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long UnissuedReceiverPolicyEpoch = 0;
    internal ReplicatedOperation CreateVerifiedPartitionMovementReceiverIssue(Guid commandId, string principalId,
        PartitionMoveReceiverIssueBody incoming, ReadExecutionBudget work)
    {
        movementCheckpointVerifier.RequireReceiverAdministrator(this, principalId, work);
        if (commandId == Guid.Empty || incoming.ActualReceiverPrincipalId is not null
            || incoming.ActualReceiverPolicyEpoch != UnissuedReceiverPolicyEpoch || incoming.Version != PartitionMoveProtocol.Version)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority); }
        var owned = NativeSerialization.Deserialize<PartitionMoveReceiverIssueBody>(NativeSerialization.Serialize(incoming));
        var principal = Store.Read(view => RequireMoveDispatchPrincipal(work.CreateView(view), principalId,
            EvaluationClock.GetUtcNow()));
        if (commandId != PartitionMoveReceiverIssuanceIdentity.For(owned.OriginalPhaseCommandId))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var original = owned.OriginalEnvelope;
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, Limits.MaxBatchBytes);
        if (original.ReceiverIssuanceProof is not null || original.Grant is not { RequireReceiverIssuance: true } grant
            || commandId == owned.OriginalPhaseCommandId || commandId == grant.GrantId || commandId == original.MoveId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var catalog = Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(work.CreateView(view)))
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        PartitionMoveGrantValidation.Require(original, owned.OriginalPhaseCommandId, catalog.DefaultShard,
            EvaluationClock.GetUtcNow());
        Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var current = RequireMoveDispatchPrincipal(charged, principal.Id, EvaluationClock.GetUtcNow());
            var previous = PartitionMoveReceiverIssuanceStorage.Read(charged, original.Partition, original.MoveId,
                owned.OriginalPhaseCommandId, Limits.MaxBatchBytes);
            if (previous is not null)
            {
                if (previous.ReceiverPrincipalId != current.Id)
                { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
                PartitionMoveReceiverIssuanceValidation.Require(previous, owned.OriginalPhaseCommandId,
                    original, owned.OriginalAuthorization, catalog.DefaultShard, Limits.MaxBatchBytes);
                throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict);
            }
            if (current.PolicyEpoch != principal.PolicyEpoch)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
            return true;
        });
        owned = owned with { ActualReceiverPrincipalId = principal.Id, ActualReceiverPolicyEpoch = principal.PolicyEpoch };
        var payload = NativeSerialization.Serialize(owned);
        RequireNativeBudget(payload.Length);
        var proof = movementCheckpointVerifier.VerifyReceiverIssue(this, principal.Id, payload, work);
        if (proof != Convert.ToHexStringLower(SHA256.HashData(payload)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.MissingAuthority); }
        var phase = new PartitionMovePhaseCommand(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner, original.ControlIntentDigest,
            PartitionMovePeerStage.ReceiverIssue, original.PageOrdinal, payload, Resources: ImmutableArray<ResourceDefinition>.Empty);
        work.Check();
        return IssueNativeOperation(new(commandId, OperationKind.PartitionMovementPhase, principal.Id,
            EvaluationClock.GetUtcNow(), MovementPhaseIdentityJson(phase)), new NativeCommandPayload(NativeSerialization.Serialize(phase)));
    }
}
