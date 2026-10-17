namespace KeyLoad.Core;

// Call-local successfully authenticated admission; never persisted or caller supplied.
internal sealed record EventVectorFailedOpenEligibility(EventVectorParentOutcomeIndex Index,
    Guid Incarnation, long PolicyEpoch);
