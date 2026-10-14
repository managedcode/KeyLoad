
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionPausePublicState(ReceiveSubscriptionRequest Claim, ReceiveSubscriptionResult Claimed,
    SetSubscriptionPausedRequest Pause, SubscriptionInfo Paused,
    ReceiveSubscriptionRequest Denied, string DeniedProblem, EventSourcePage Source)
{
    internal List<(SubscriptionDeliveryCommand Request, CommitReceipt Receipt)> History { get; } = [];
    internal SetSubscriptionPausedRequest? Resume
    {
        get; set;
    }
    internal SubscriptionInfo? Resumed
    {
        get; set;
    }
}
