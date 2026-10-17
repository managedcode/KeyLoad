using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

internal static class EventVectorObservedPhase
{
    private const string InvalidObservation = "The actual original event vector source observation is inconsistent.";

    internal static EventVectorSourcePhase Create(EventVectorSourcePhase admitted,
        EventVectorSourceObservation observation, ReadOnlyMemory<byte> authenticatedReplyWitness,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(admission);
        var mutation = EventVectorSourceMutationValidation.ReadOriginal(admitted, admission);
        admission.RequireEncodedBytes(authenticatedReplyWitness.Length);
        admission.RequireEncodedBytes(observation.OriginalStoredOutcome.Length);
        if (authenticatedReplyWitness.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidObservation);
        }
        EventVectorObservationIdentity.Require(admitted, observation);
        if (observation.OriginalStoredOutcome.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidObservation);
        }
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(observation.OriginalStoredOutcome.Span);
        var originalJson = JsonSerializer.SerializeToUtf8Bytes(mutation, JsonDefaults.Options);
        admission.RequireEncodedBytes(originalJson.LongLength);
        var fingerprint = NativeOperationFingerprint.Compute(admitted.PhaseCommandId,
            OperationKind.EventFeedSourcePhase, admitted.PrincipalId, originalJson);
        if (outcome.Fingerprint != fingerprint || outcome.Incarnation != admitted.SourceOwner.Incarnation
            || outcome.PolicyEpoch != admitted.SourcePolicyEpoch || outcome.ScopeKind != CommandOutcomeScopeKind.Partition
            || outcome.Partition != admitted.Source.Partition || outcome.Result is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidObservation);
        }
        EventVectorObservedResult.Require(admitted, mutation, outcome.Result,
            observation.AppliedCutPosition, admission);
        var observed = admitted with
        {
            SourceOriginalResult = outcome.Result,
            SourceOriginalOutcomeDigest = SHA256.HashData(observation.OriginalStoredOutcome.Span),
            SourceObservationWitness = authenticatedReplyWitness.ToArray(),
            Disposition = EventVectorSourcePhaseDisposition.Observed
        };
        admission.RequireEncodedBytes(NativeSerialization.Measure(observed));
        _ = EventVectorSourceMutationValidation.ReadObserved(observed, admission);
        return observed;
    }
}
