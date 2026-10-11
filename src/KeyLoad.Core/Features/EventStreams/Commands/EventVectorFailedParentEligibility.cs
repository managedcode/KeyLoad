namespace KeyLoad.Core;

internal sealed record EventVectorFailedParentEligibility(EventVectorFailedOpenEligibility? Open,
    EventVectorFailedCleanupEligibility? Cleanup);
