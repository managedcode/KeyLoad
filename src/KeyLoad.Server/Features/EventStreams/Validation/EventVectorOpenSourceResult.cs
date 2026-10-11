using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorOpenSourceResult
{
    internal static EventVectorSourcePhase Require(object result, EventVectorSourcePhase admitted,
        EventVectorSourcePhase observed, EventVectorAdmissionPolicy admission)
    {
        if (result is not EventVectorControlResult control || control.SourcePhase is not { } actual
            || control.Receipt is null || control.Receipt.CommandId != control.CommandId
            || actual.PhaseCommandId != admitted.PhaseCommandId
            || actual.SourceOrdinal != admitted.SourceOrdinal || actual.Source != admitted.Source
            || actual.Disposition != EventVectorSourcePhaseDisposition.Observed
            || !actual.OriginalNativeBody.Span.SequenceEqual(admitted.OriginalNativeBody.Span)
            || !actual.SourceOriginalOutcomeDigest.Span.SequenceEqual(observed.SourceOriginalOutcomeDigest.Span)
            || !actual.SourceObservationWitness.Span.SequenceEqual(observed.SourceObservationWitness.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid); }
        var mutation = EventVectorSourceMutationValidation.ReadObserved(actual, admission);
        if (actual.SourceOriginalResult is null || actual.SourceOriginalResult.Error is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid); }
        if (actual.Role == EventVectorSourcePhaseRole.Release)
        {
            EventVectorReleaseResultValidation.Require(actual, mutation,
                actual.SourceOriginalResult.Get<EventVectorSourceReleaseResult>(), admission);
        }
        else
        {
            EventVectorObservedPinValidation.Require(actual, mutation,
                actual.SourceOriginalResult.Get<EventVectorSourceResult>(), admission);
        }
        return actual;
    }
}
