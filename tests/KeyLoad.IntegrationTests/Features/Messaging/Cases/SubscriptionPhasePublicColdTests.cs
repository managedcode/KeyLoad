
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class SubscriptionPhasePublicColdTests
{
    [Test]
    public Task GenerationTwoChangedEffectsRefusesThenOriginalInboxCompletesAcrossSdkMcpAndTwoColdCuts()
        => SubscriptionPhasePublicTrial.RunAsync(SubscriptionInboxPublicFlow.RunAsync);

    [Test]
    public Task ManualPauseKeepsCurrentLeaseDrainThenSameGenerationFreshReceiveAcrossSdkMcpAndTwoColdCuts()
        => SubscriptionPhasePublicTrial.RunAsync(SubscriptionPausePublicFlow.RunAsync);
}
