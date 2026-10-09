using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

/// <summary>Exact first receiver scope, independent from current read nonce and appended proof transport bytes.</summary>
internal static class PartitionMoveReceiverIssuanceValidation
{
    private const int UnissuedPolicyEpoch = 0;
    private const int UnappliedPosition = 0;
    private const int EmptyFingerprintLength = 0;
    internal static void Require(PartitionMoveReceiverIssuance value, Guid originalId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt originalAuthorization,
        PhysicalShardRecord receiver, int maximumBytes)
    {
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, maximumBytes);
        var grant = original.Grant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        if (value.Version != PartitionMoveProtocol.Version || originalId == Guid.Empty
            || value.OriginalPhaseCommandId != originalId || grant.PhaseCommandId != originalId
            || value.MoveId != original.MoveId || value.Partition != original.Partition
            || value.OriginalStage != original.Stage || value.OriginalPageOrdinal != original.PageOrdinal
            || PartitionMoveGrantValidation.IsLocalControl(original.Stage) || !grant.RequireReceiverIssuance
            || value.OriginalRequestNonce != original.Nonce || value.OriginalRequestNonce == Guid.Empty
            || value.OriginalGrantId != grant.GrantId || grant.GrantId == Guid.Empty
            || value.SourceOperatorPrincipalId != grant.OperatorPrincipalId
            || value.SourceOperatorPolicyEpoch != grant.OperatorPolicyEpoch
            || string.IsNullOrWhiteSpace(value.ReceiverPrincipalId) || value.ReceiverIssuancePolicyEpoch <= UnissuedPolicyEpoch
            || value.OriginalExpiresAt != original.ExpiresAt || value.OriginalExpiresAt != grant.ExpiresAt
            || value.IssuanceCommandId == Guid.Empty || value.IssuanceCommandId == originalId
            || value.IssuanceAppliedPosition <= UnappliedPosition || value.IssuanceFingerprint.Length == EmptyFingerprintLength
            || value.OriginalBodyDigest != grant.BodyDigest
            || value.OriginalControlIntentDigest != original.ControlIntentDigest
            || value.OriginalPhaseIdentityDigest != PartitionMoveOriginalDispatchIdentity.Digest(originalId, original)
            || !PhysicalOwnerEntryValidation.SameOwner(value.ReceiverOwner, receiver)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, receiver)
            || !NativeSerialization.Serialize(value.OriginalAuthorization).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(originalAuthorization))
            || originalAuthorization.CommandId != grant.GrantId
            || originalAuthorization.AppliedPosition != grant.AdmissionPosition
            || !PhysicalOwnerEntryValidation.SameOwner(originalAuthorization.PhysicalOwner, grant.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority); }
    }

    internal static void RequireStoredResult(PartitionMoveReceiverIssuance value, StoredOutcome actual,
        Guid incarnation, long currentReadCut)
    {
        if (actual.Incarnation != incarnation || actual.PolicyEpoch != value.ReceiverIssuancePolicyEpoch
            || actual.Fingerprint != value.IssuanceFingerprint || actual.Result.Error is not null
            || currentReadCut < value.IssuanceAppliedPosition)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        var result = actual.Result.Get<PartitionMovePhaseResult>();
        if (result.Stage != PartitionMovePeerStage.ReceiverIssue || result.MoveId != value.MoveId
            || result.Journal.CommandId != value.IssuanceCommandId
            || result.Journal.AppliedPosition != value.IssuanceAppliedPosition
            || result.Journal.ControlIntentDigest != value.OriginalControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(result.Journal.PhysicalOwner, value.ReceiverOwner)
            || !NativeSerialization.Serialize(result.ReceiverIssuance).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(value)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
    }
}
