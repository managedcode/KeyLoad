namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueProducerClusterOriginal(QueueProducerClusterSeed Seed, CommitReceipt Receipt,
    QueueProducerClusterImage Image);
