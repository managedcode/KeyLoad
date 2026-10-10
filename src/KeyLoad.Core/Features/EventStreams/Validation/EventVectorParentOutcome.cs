using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorParentOutcome
{
    private const int Version = 1;
    private const int AbsentValue = 0;
    private const long FirstPosition = 1;
    private const string MissingOutcome = "The original event vector native outcome requires recovery.";

    internal static StoredOutcome Require(IKeyValueView view, PrincipalRecord principal,
        EventVectorControlPhase phase, Guid commandId, string? expectedFingerprint,
        Guid incarnation, EventVectorAdmissionPolicy admission)
    {
        var identity = EventVectorControlPhaseIds.Project(phase, principal.Id, admission);
        var actualId = EventVectorControlPhaseIds.For(phase, principal.Id, identity.OriginalBodyDigest, admission);
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, phase.OriginalRequest.ControlPartition);
        var outcome = CommandOutcomeKeyResolver.Select(view, principal.Id, commandId, scope).Outcome;
        var metadata = JsonSerializer.Serialize(identity, JsonDefaults.Options);
        var original = new ReplicatedOperation(commandId, OperationKind.EventFeedControl, principal.Id, default, metadata);
        var fingerprint = NativeOperationFingerprint.Compute(original);
        if (actualId != commandId || outcome is null || outcome.Incarnation != incarnation
            || outcome.PolicyEpoch != principal.PolicyEpoch || outcome.Fingerprint != fingerprint
            || expectedFingerprint is not null && expectedFingerprint != fingerprint)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, MissingOutcome); }
        if (outcome.Result.Error is null)
        { RequireResult(outcome.Result.Get<EventVectorControlResult>(), commandId, phase, incarnation); }
        else if (phase.Action != EventVectorControlAction.Open || outcome.Result.NativeValue is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, MissingOutcome); }
        return outcome;
    }

    private static void RequireResult(EventVectorControlResult result, Guid commandId, EventVectorControlPhase phase, Guid incarnation)
    {
        var sourceAction = phase.Action is EventVectorControlAction.AdmitSource or EventVectorControlAction.ObserveSource;
        var offerAction = phase.Action == EventVectorControlAction.Offer;
        var count = (result.Map is null ? AbsentValue : Version) + (result.Offer is null ? AbsentValue : Version) + (result.SourcePhase is null ? AbsentValue : Version);
        if (result.Version != Version || result.CommandId != commandId || count != Version
            || sourceAction != (result.SourcePhase is not null) || offerAction != (result.Offer is not null)
            || result.Receipt.CommandId != commandId || result.Receipt.Token.Incarnation != incarnation
            || result.Receipt.Token.Position < FirstPosition || result.Receipt.Token.AtomicPartitionId != phase.OriginalRequest.ControlPartition.AtomicPartitionId
            || result.Map is { } map && (map.MapId != phase.OriginalRequest.MapId || map.ControlPartition != phase.OriginalRequest.ControlPartition)
            || result.Offer is { } offer && offer.MapId != phase.OriginalRequest.MapId
            || result.SourcePhase is { } source && source.MapId != phase.OriginalRequest.MapId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, MissingOutcome); }
    }
}
