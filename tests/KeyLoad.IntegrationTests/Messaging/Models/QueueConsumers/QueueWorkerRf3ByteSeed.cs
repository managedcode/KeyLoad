namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueWorkerRf3ByteSeed(QueueWorkerRf3Seed Native, string Payload, string Projected, int ProjectedBytes)
{
    public override string ToString() => "QueueWorkerRf3ByteSeed(<private>)";
}
