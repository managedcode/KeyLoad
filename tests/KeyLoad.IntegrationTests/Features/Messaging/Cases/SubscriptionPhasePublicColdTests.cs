
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class SubscriptionPhasePublicColdTests
{
    [Test]
    public Task GenerationTwoChangedEffectsRefusesThenOriginalInboxCompletesAcrossSdkMcpAndTwoColdCuts()
        => SubscriptionPhasePublicTrial.RunAsync(SubscriptionInboxPublicFlow.RunAsync);

    [Test]
    public Task ManualPauseKeepsCurrentLeaseDrainThenSameGenerationFreshReceiveAcrossSdkMcpAndTwoColdCuts()
        => SubscriptionPhasePublicTrial.RunAsync(SubscriptionPausePublicFlow.RunAsync);

    [Test]
    public Task CachedEmptyReceiveRemainsOriginalAfterPublishThenFreshClaimSeekFenceAndTwoColdCuts()
        => SubscriptionPhasePublicTrial.RunAsync(SubscriptionCachedEmptyPublicFlow.RunAsync);

    [Test]
    public Task CachedEmptyAndDeniedRequestsRemainEpochFencedAfterPolicyRestoreTwoColdAndFreshHealthyClaims()
        => SubscriptionPhasePublicTrial.RunAsync(SubscriptionCachedEmptyPolicyFlow.RunAsync);

    [Test]
    public Task PersistedNackGapKeepsOriginalReceiptsAcrossTwoColdCutsThenOnlyReclaimedHeadReleasesHealthyFollower()
        => SubscriptionPhasePublicTrial.RunAsync(SubscriptionNackGapPublicFlow.RunAsync);

    [Test]
    public Task ConcurrentSdkAndMcpOriginalRequestClaimsOnceThenWindowBackpressureColdGapAndHealthyContinuation()
        => SubscriptionPhasePublicTrial.RunAsync(SubscriptionConcurrentRequestPublicFlow.RunAsync);
}
