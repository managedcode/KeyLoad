using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorCleanupSourceBinding
{
    private const long AbsentRevision = 0;
    private const int FirstOrdinal = 0;
    private const string Invalid = "The cleanup source does not match its retained original pin.";

    internal static void Require(EventVectorControlPhase control, EventVectorSourcePhase intended,
        EventVectorAdmissionPolicy admission)
    {
        admission.RequireEncodedBytes(control.OriginalCleanupFrontierBytes.Length);
        var frontier = NativeSerialization.Deserialize<ImmutableArray<EventVectorCleanupPin>>(
            control.OriginalCleanupFrontierBytes.Span);
        if (frontier.IsDefault || intended.SourceOrdinal < FirstOrdinal || intended.SourceOrdinal >= frontier.Length
            || intended.Role != EventVectorSourcePhaseRole.Release)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        admission.RequireEntryCount(frontier.Length);
        var retained = frontier[intended.SourceOrdinal];
        if (retained is null || retained.OriginalPinPhaseBytes.IsEmpty
            || retained.AuthenticatedSourceObservationBytes.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var pin = NativeSerialization.Deserialize<EventVectorSourcePhase>(retained.OriginalPinPhaseBytes.Span);
        var observation = NativeSerialization.Deserialize<EventVectorSourceObservation>(
            retained.AuthenticatedSourceObservationBytes.Span);
        EventVectorObservationIdentity.Require(pin, observation);
        var mutation = intended.Disposition == EventVectorSourcePhaseDisposition.Admitted
            ? EventVectorSourceMutationValidation.ReadOriginal(intended, admission)
            : EventVectorSourceMutationValidation.ReadObserved(intended, admission);
        var current = observation.CurrentPinRowBytes.IsEmpty ? null
            : NativeSerialization.Deserialize<EventVectorSourcePin>(observation.CurrentPinRowBytes.Span);
        var expectedPosition = current?.Position ?? pin.TargetPosition;
        var expectedCoverage = current?.CoverageGeneration ?? pin.CoverageGeneration;
        if (pin.Role != EventVectorSourcePhaseRole.Pin || intended.Source != pin.Source
            || mutation.OriginalPinCommandId != pin.PhaseCommandId
            || !mutation.OriginalPinNativeBody.Span.SequenceEqual(pin.OriginalNativeBody.Span)
            || mutation.ExpectedPinRevision != (current?.PinRevision ?? AbsentRevision)
            || mutation.ExpectedCoverageGeneration != (current?.CoverageGeneration ?? AbsentRevision)
            || intended.CoverageGeneration != expectedCoverage
            || intended.OriginalPosition != expectedPosition || intended.TargetPosition != expectedPosition)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
