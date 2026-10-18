namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueWorkerClusterSeed(QueueLaneRef Lane, MessagingClusterIdentity Identity,
    CommandRequest Producer, CommitReceipt Receipt, byte[] SigningKey)
{
    public override string ToString() => "QueueWorkerClusterSeed(<private>)";
}
