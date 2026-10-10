namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueOrderedRetryPublicState(QueueLifecyclePublicState Caller, QueueParkedHeadPolicy ParkedHead)
{
    internal Delivery? Original { get; set; }
    internal Delivery? Second { get; set; }
}
