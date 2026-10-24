namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueWorkerClusterByteSeed(QueueWorkerClusterSeed Execution, string Payload, string Projected, int ProjectedBytes)
{
    public override string ToString() => "QueueWorkerClusterByteSeed(<private>)";
}
