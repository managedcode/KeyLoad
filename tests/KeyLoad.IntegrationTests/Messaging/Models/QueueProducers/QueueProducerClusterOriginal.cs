namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueProducerClusterOriginal(QueueProducerClusterSeed Seed, CommitReceipt Receipt,
    QueueProducerClusterImage Image);
