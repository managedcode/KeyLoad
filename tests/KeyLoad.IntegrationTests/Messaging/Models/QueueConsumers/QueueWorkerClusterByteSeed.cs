namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueWorkerClusterByteSeed(QueueWorkerClusterSeed Native, string Payload, string Projected, int ProjectedBytes)
{
    public override string ToString() => "QueueWorkerClusterByteSeed(<private>)";
}
