namespace KeyLoad.Core;

// Distinct call-local eligibility; never persisted or supplied by callers.
internal sealed record EventVectorFailedCleanupEligibility(EventVectorParentOutcomeIndex Index,
    Guid Incarnation, long PolicyEpoch);

