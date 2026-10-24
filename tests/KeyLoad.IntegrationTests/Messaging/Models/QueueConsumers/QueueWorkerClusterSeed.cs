namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueWorkerClusterSeed(QueueLaneRef Lane, MessagingClusterIdentity Identity,
    CommandRequest Producer, CommitReceipt Receipt, byte[] SigningKey)
{
    public override string ToString() => "QueueWorkerClusterSeed(<private>)";
}
