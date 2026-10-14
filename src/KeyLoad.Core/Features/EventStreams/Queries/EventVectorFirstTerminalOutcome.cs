using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstTerminalOutcome
{
    private const int Version = 1;
    private const long FirstPosition = 1;
    private const string Invalid = "The original event vector first issuance terminal outcome requires recovery.";

    internal static void Require(IKeyValueView view, PrincipalRecord principal,
        EventVectorFirstIssuance row, Guid incarnation, EventVectorAdmissionPolicy admission)
    {
        admission.RequireEncodedBytes(row.LastCleanupNativePayloadBytes.Length);
        var encoded = new ReplicatedOperation(row.LastCleanupNativeCommandId, OperationKind.EventFeedControl,
            principal.Id, default, string.Empty)
        {
            NativePayload = row.LastCleanupNativePayloadBytes
        };
        var phase = NativeCommandPayload.Read<EventVectorControlPhase>(encoded);
        EventVectorControlFields.Require(phase, admission);
        var identity = EventVectorControlOperationIdentity.Create(phase, principal.Id, admission);
        var operation = encoded with
        {
            PayloadJson = identity.Metadata
        };
        if (phase.Action != EventVectorControlAction.CleanupAbandonedIssuance
            || identity.CommandId != row.LastCleanupNativeCommandId
            || phase.OriginalFirstIssuancePublicCommandId != row.OriginalPublicCommandId
            || phase.OriginalRequest.MapId != row.MapId
            || phase.OriginalRequest.ControlPartition != row.ControlPartition
            || row.LastCleanupNativeFingerprint != NativeOperationFingerprint.Compute(operation))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, identity.CommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, row.ControlPartition));
        var outcome = selected.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        if (outcome.Incarnation != incarnation || outcome.PolicyEpoch != principal.PolicyEpoch
            || outcome.Fingerprint != row.LastCleanupNativeFingerprint || outcome.Result.Error is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var result = outcome.Result.Get<EventVectorFirstIssuanceResult>();
        if (result.Version != Version || result.NativeCommandId != identity.CommandId
            || result.Issuance is null || result.Receipt is null
            || result.Issuance.Version != row.Version
            || result.Issuance.OriginalParentExpiresAt != row.OriginalParentExpiresAt
            || !result.Issuance.OriginalAuthorizedSourceScopes.SequenceEqual(row.OriginalAuthorizedSourceScopes)
            || !result.Issuance.OriginalPublicRequestBytes.Span.SequenceEqual(row.OriginalPublicRequestBytes.Span)
            || !result.Issuance.OriginalCoverageWitnessBytes.Span.SequenceEqual(row.OriginalCoverageWitnessBytes.Span)
            || result.Issuance.State != EventVectorFirstIssuanceState.Released
            || result.Issuance.PrincipalId != row.PrincipalId
            || result.Issuance.OriginalPublicCommandId != row.OriginalPublicCommandId
            || result.Issuance.MapId != row.MapId || result.Issuance.ControlPartition != row.ControlPartition
            || result.Issuance.LastCleanupNativeCommandId != row.LastCleanupNativeCommandId
            || result.Issuance.LastCleanupNativeFingerprint != row.LastCleanupNativeFingerprint
            || !result.Issuance.LastCleanupNativePayloadBytes.Span.SequenceEqual(row.LastCleanupNativePayloadBytes.Span)
            || !result.Issuance.OriginalCapturedFirstOpenPhaseBytes.Span.SequenceEqual(row.OriginalCapturedFirstOpenPhaseBytes.Span)
            || !result.Issuance.OriginalPublicRequestDigest.Span.SequenceEqual(row.OriginalPublicRequestDigest.Span)
            || result.Receipt.CommandId != identity.CommandId || result.Receipt.Token.Incarnation != incarnation
            || result.Receipt.Token.AtomicPartitionId != row.ControlPartition.AtomicPartitionId
            || result.Receipt.Token.Position < FirstPosition)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
    }
}
