namespace KeyLoad.Core;

internal static class EventVectorSettledSourceMutation
{
    private const string NonceFormat = "N";
    private const string Invalid = "The original settled event vector source mutation is inconsistent.";

    internal static EventVectorSourceMutation Create(EventVectorMap map, EventVectorEntry current,
        EventVectorEntry target, EventVectorSourcePhase settled, EventVectorSourcePhaseRole role,
        DateTimeOffset originalParentExpiry, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(settled);
        ArgumentNullException.ThrowIfNull(admission);
        if (role is not (EventVectorSourcePhaseRole.Advance or EventVectorSourcePhaseRole.Release)
            || settled.Disposition != EventVectorSourcePhaseDisposition.Observed
            || settled.SourceOriginalResult is null || settled.SourceOriginalResult.Error is not null
            || current.Source != target.Source || current.Source != settled.Source
            || target.Position < current.Position || role == EventVectorSourcePhaseRole.Release && target != current)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var original = EventVectorSourceMutationValidation.ReadObserved(settled, admission);
        var result = settled.SourceOriginalResult.Get<EventVectorSourceResult>();
        EventVectorObservedPinValidation.Require(settled, original, result, admission);
        var pin = result.Pin;
        if (pin.Position != current.Position || pin.CoverageGeneration != map.CoverageGeneration
            || pin.PrincipalId != map.PrincipalId || pin.ControlIncarnation != map.ControlIncarnation
            || originalParentExpiry == default)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid); }
        var firstBody = settled.Role == EventVectorSourcePhaseRole.Pin
            ? settled.OriginalNativeBody : original.OriginalPinNativeBody;
        admission.RequireEncodedBytes(firstBody.Length);
        return original with
        {
            Role = role,
            ExpectedPinRevision = pin.PinRevision,
            ExpectedCoverageGeneration = pin.CoverageGeneration,
            Position = target.Position,
            CoverageGeneration = map.CoverageGeneration,
            OriginalPinCommandId = pin.OriginalPinCommandId,
            OriginalPinNativeBody = firstBody,
            FirstExpiresAt = originalParentExpiry,
            Nonce = Guid.NewGuid().ToString(NonceFormat),
            CleanupGeneration = role == EventVectorSourcePhaseRole.Release
                ? map.CleanupGeneration : EventVectorSourcePhaseIds.InitialCleanupGeneration
        };
    }
}
