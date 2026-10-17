namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueProducerRf3Original(QueueProducerRf3Seed Seed, CommitReceipt Receipt,
    QueueProducerRf3Image Image);
