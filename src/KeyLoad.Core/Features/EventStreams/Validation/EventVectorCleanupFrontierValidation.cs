using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorCleanupFrontierValidation
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;
    private const string Invalid = "The complete authenticated cleanup frontier is inconsistent.";

    internal static ImmutableArray<EventVectorCleanupPin> Require(EventVectorOwnedInventory inventory,
        ReadOnlyMemory<byte> frontierBytes, PrincipalRecord principal, IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        if (principal.Id != map.PrincipalId || principal.PolicyEpoch != map.ControlPolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, Invalid); }
        admission.RequireEncodedBytes(frontierBytes.Length);
        if (frontierBytes.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var retained = EventVectorCleanupRetainedPins.Read(inventory, admission);
        foreach (var row in retained)
        {
            var pin = NativeSerialization.Deserialize<EventVectorSourcePhase>(row.Value.Span);
            authorization.Require(principal, pin.Source.Partition, pin.Source.Resource, Capability.SubscriptionsManage);
            authorization.Require(principal, pin.Source.Partition, pin.Source.Resource,
                pin.Source.Kind == EventSourceKind.Stream ? Capability.EventsRead : Capability.TopicsRead);
        }
        var frontier = NativeSerialization.Deserialize<ImmutableArray<EventVectorCleanupPin>>(frontierBytes.Span);
        if (frontier.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        admission.RequireEntryCount(frontier.Length);
        if (frontier.Length != retained.Length)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        for (var ordinal = FirstOrdinal; ordinal < retained.Length; ordinal++)
        {
            var supplied = frontier[ordinal];
            if (supplied is null || supplied.Version != Version
                || !supplied.OriginalPinPhaseBytes.Span.SequenceEqual(retained[ordinal].Value.Span))
            { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
            var original = NativeSerialization.Deserialize<EventVectorSourcePhase>(retained[ordinal].Value.Span);
            RequirePin(inventory, original, supplied, admission);
        }
        return frontier;
    }

    private static void RequirePin(EventVectorOwnedInventory inventory, EventVectorSourcePhase original,
        EventVectorCleanupPin supplied, EventVectorAdmissionPolicy admission)
    {
        if (!supplied.LastAdmittedAdvancePhaseBytes.Span.SequenceEqual(
                EventVectorCleanupRetainedPins.LastAdvance(original, inventory, admission).Span))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        admission.RequireEncodedBytes(supplied.AuthenticatedSourceObservationBytes.Length);
        if (supplied.AuthenticatedSourceObservationBytes.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var observation = NativeSerialization.Deserialize<EventVectorSourceObservation>(
            supplied.AuthenticatedSourceObservationBytes.Span);
        EventVectorObservationIdentity.Require(original, observation);
        if (original.Disposition == EventVectorSourcePhaseDisposition.Observed
            && (observation.OriginalStoredOutcome.IsEmpty || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(observation.OriginalStoredOutcome.Span), original.SourceOriginalOutcomeDigest.Span)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        RequireCurrent(original, supplied, observation, admission);
    }

    private static void RequireCurrent(EventVectorSourcePhase original, EventVectorCleanupPin supplied,
        EventVectorSourceObservation observation, EventVectorAdmissionPolicy admission)
    {
        admission.RequireEncodedBytes(observation.CurrentPinRowBytes.Length);
        if (observation.CurrentPinRowBytes.IsEmpty)
        {
            if (supplied.CurrentCoverageGeneration is not null || supplied.CurrentPinRevision is not null)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            return;
        }
        var current = NativeSerialization.Deserialize<EventVectorSourcePin>(observation.CurrentPinRowBytes.Span);
        if (current.Version != Version || current.MapId != original.MapId
            || current.ControlPartition != original.ControlPartition
            || current.ControlIncarnation != original.ControlIncarnation || current.Source != original.Source
            || current.PrincipalId != original.PrincipalId || current.SourcePolicyEpoch != original.SourcePolicyEpoch
            || current.SourceSchemaVersion != original.SourceSchemaVersion
            || current.OriginalPinCommandId != original.PhaseCommandId
            || current.OriginalPinExpiresAt != original.FirstExpiresAt
            || !CryptographicOperations.FixedTimeEquals(current.OriginalPinBodyDigest.Span, original.OriginalBodyDigest.Span)
            || current.CoverageGeneration != supplied.CurrentCoverageGeneration
            || current.PinRevision != supplied.CurrentPinRevision || current.Position < original.TargetPosition)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
