namespace KeyLoad.Core;

internal static class EventVectorObservedResult
{
    private const long EmptyPosition = 0;
    private const string InvalidResult = "The actual native event vector source result is inconsistent.";

    internal static void Require(EventVectorSourcePhase phase, EventVectorSourceMutation request,
        OperationResult result, long actualAppliedCut, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(admission);
        if (result.Error is not null)
        { return; }
        CommitToken token;
        if (phase.Role == EventVectorSourcePhaseRole.Release)
        {
            var released = result.Get<EventVectorSourceReleaseResult>();
            EventVectorReleaseResultValidation.Require(phase, request, released, admission);
            token = released.Token;
        }
        else
        {
            var pinned = result.Get<EventVectorSourceResult>();
            EventVectorObservedPinValidation.Require(phase, request, pinned, admission);
            token = pinned.Token;
        }
        if (token is null || token.Position <= EmptyPosition || token.Position > actualAppliedCut)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidResult); }
    }
}
