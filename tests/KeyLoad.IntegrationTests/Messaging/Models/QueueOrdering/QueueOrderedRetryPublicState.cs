namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueOrderedRetryPublicState(QueueLifecyclePublicState Caller, QueueParkedHeadPolicy ParkedHead)
{
    internal Delivery? Original
    {
        get; set;
    }
    internal Delivery? Second
    {
        get; set;
    }
    internal long ParkedOrder => ParkedHead == QueueParkedHeadPolicy.Block ? QueueOrderedRetryPublicProtocol.One : QueueOrderedRetryPublicProtocol.Initial;
    internal long RedrivenOrder => ParkedHead == QueueParkedHeadPolicy.Block ? QueueOrderedRetryPublicProtocol.One : QueueOrderedRetryPublicProtocol.Three;
    internal long HealthyEnqueue => ParkedHead == QueueParkedHeadPolicy.Block ? QueueOrderedRetryPublicProtocol.Three : QueueOrderedRetryPublicProtocol.Four;
}
