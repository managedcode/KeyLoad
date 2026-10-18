namespace KeyLoad.UnitTests.Features.ResourceExecution;

// Native crypto/codec boundary proof only; these cases do not execute retirement or claim live CLUSTER membership.
internal sealed class ResourceRetirementNativeEnvelopeTests
{
    [Test]
    public Task StableCommandAcrossFreshRpcAttemptsRequiresExactOriginalRequestMacInEachReply()
        => ResourceRetirementNativeEnvelopeFlow.CorrelationAsync();

    [Test]
    public Task OriginalSignedRequestRejectsCommandModeBodyAndOwnerTamperingThenOriginalRemainsHealthy()
        => ResourceRetirementNativeEnvelopeFlow.TamperingAsync();

    [Test]
    public Task IndependentNativeDigestsKeepRetirementRequestReplyAndInventoryPurposesSeparate()
        => ResourceRetirementNativeEnvelopeFlow.DomainsAsync();

    [Test]
    public Task NativeMacFailurePrecedesElapsedExpiryForRequestsAndDelayedRepliesThenFreshProofIsHealthy()
        => ResourceRetirementNativeEnvelopeExpiry.RunAsync();
}
