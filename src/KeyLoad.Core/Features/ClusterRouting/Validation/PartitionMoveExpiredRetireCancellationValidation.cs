using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Only distinct transport material is excluded; first native cancellation authority is immutable.</summary>
    private ReadOnlyMemory<byte> CanonicalMoveRetireCancellationBody(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty || body.Length > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var value = NativeSerialization.Deserialize<PartitionMoveRetireCancellationBody>(body.Span);
        var canonical = value with
        {
            OriginalRequestBytes = ReadOnlyMemory<byte>.Empty,
            OriginalRequestSignature = string.Empty,
            CancellationExpiresAt = default,
            OriginalSourceWitness = null
        };
        if (NativeSerialization.Measure(canonical) > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        return NativeSerialization.Serialize(canonical);
    }
    private void RequireMoveRetireCancellationScope(IKeyValueView view,
        PartitionMoveRetireCancellationBody body, string principalId, long policyEpoch, DateTimeOffset now)
    {
        var original = body.OriginalEnvelope;
        ValidatePartition(original.Partition);
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, Limits.MaxBatchBytes);
        var grant = original.Grant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (body.Version != PartitionMoveProtocol.Version || original.Stage != PartitionMovePeerStage.Retire
            || original.ReceiverIssuanceProof is not null || original.SourceDispatchWitness is not null
            || original.ExpiresAt > now || body.CancellationExpiresAt <= now
            || body.OriginalPhaseCommandId == Guid.Empty || body.CancellationCommandId == Guid.Empty
            || body.CancellationCommandId == body.OriginalPhaseCommandId || body.CancellationCommandId == original.MoveId
            || body.CancellationCommandId == grant.GrantId || body.CleanupGeneration < PartitionMoveProtocol.EmptyCount
            || body.ActualReceiverPrincipalId != principalId || body.ActualReceiverPolicyEpoch != policyEpoch
            || grant.Version != PartitionMoveProtocol.Version || grant.GrantId == Guid.Empty
            || grant.PhaseCommandId != body.OriginalPhaseCommandId || grant.MoveId != original.MoveId
            || grant.Partition != original.Partition || grant.Stage != PartitionMovePeerStage.Retire
            || grant.PageOrdinal != original.PageOrdinal || grant.ControlIntentDigest != original.ControlIntentDigest
            || !grant.RequireReceiverIssuance || grant.OperatorPolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || string.IsNullOrWhiteSpace(grant.OperatorPrincipalId)
            || grant.AdmissionPosition <= PartitionMoveProtocol.EmptyCount || grant.Settlement is not null
            || grant.AbortDisposition is not null || grant.RetireCancellationDisposition is not null || grant.ExpiresAt != original.ExpiresAt
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ControlOwner, original.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, catalog.DefaultShard)
            || catalog.DefaultShard.PhysicalShardId != original.SourcePlacement.PhysicalShardId
            || catalog.DefaultShard.Incarnation != original.SourcePlacement.Incarnation
            || Store.Identity.Incarnation != original.SourcePlacement.Incarnation
            || grant.BodyDigest != Convert.ToHexStringLower(SHA256.HashData(original.Body.Span))
            || body.OriginalAuthorization.CommandId != grant.GrantId
            || body.OriginalAuthorization.AppliedPosition != grant.AdmissionPosition
            || body.OriginalAuthorization.ControlIntentDigest != original.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(body.OriginalAuthorization.PhysicalOwner, original.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        var issued = PartitionMoveReceiverIssuanceStorage.Read(view, original.Partition, original.MoveId,
            body.OriginalPhaseCommandId, Limits.MaxBatchBytes);
        if (issued is not null)
        {
            PartitionMoveReceiverIssuanceValidation.Require(issued, body.OriginalPhaseCommandId, original,
                body.OriginalAuthorization, catalog.DefaultShard, Limits.MaxBatchBytes);
            if (issued.ReceiverPrincipalId != principalId)
            { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        }
    }

    private void RequireMoveRetireNotCancelled(IKeyValueView view, Guid commandId, PartitionMovePhaseCommand phase)
    {
        if (phase.Stage != PartitionMovePeerStage.Retire)
        { return; }
        if (PartitionMoveRetireCancellationStorage.Read(view, phase.Partition, phase.MoveId,
            commandId, Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
    }

    private long RequireMoveParentCleanupGeneration(IKeyValueView view, PartitionMoveCheckpointBody body,
        PartitionMoveParentHeader header, PartitionMovePhaseCommand phase)
    {
        var retire = phase.Stage == PartitionMovePeerStage.Retire;
        if (phase.Stage == PartitionMovePeerStage.ControlAuthorize)
        {
            var authorize = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(phase.Body.Span);
            retire = authorize.Phase.Stage == PartitionMovePeerStage.Retire;
        }
        else if (phase.Stage == PartitionMovePeerStage.ControlAcknowledge)
        {
            var acknowledge = NativeSerialization.Deserialize<PartitionMoveAcknowledgeBody>(phase.Body.Span);
            var grant = PartitionMoveGrantStorage.Read(view, header.Partition, acknowledge.GrantId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            retire = grant.Stage == PartitionMovePeerStage.Retire;
        }
        var expected = retire ? header.CleanupGeneration : PartitionMoveProtocol.EmptyCount;
        if (body.CleanupGeneration != expected || body.RetireCancellation is not null || body.RetireCancellationAttempt is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
        return expected;
    }

}
