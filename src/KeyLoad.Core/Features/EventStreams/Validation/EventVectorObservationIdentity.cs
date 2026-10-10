namespace KeyLoad.Core;

internal static class EventVectorObservationIdentity
{
    private const int Version = 1;
    private const long InitialCut = 0;
    private const string InvalidObservation = "The actual original event vector source observation is inconsistent.";

    internal static void Require(EventVectorSourcePhase admitted, EventVectorSourceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Version != Version
            || observation.OriginalSourcePhaseCommandId != admitted.PhaseCommandId
            || !observation.OriginalBodyDigest.Span.SequenceEqual(admitted.OriginalBodyDigest.Span)
            || observation.Source != admitted.Source || observation.NodeId == Guid.Empty
            || observation.ReadGeneration < InitialCut || observation.StoreCutPosition < InitialCut
            || observation.AppliedCutPosition < InitialCut || observation.PrincipalId != admitted.PrincipalId
            || observation.PolicyEpoch != admitted.SourcePolicyEpoch)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidObservation); }
        EventVectorObservationPlacement.Require(admitted, observation);
    }
}
