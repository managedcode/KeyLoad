namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobGlobalQuotaR49State(PartitionRef partition, OutboxStatus outbox)
{
    internal PartitionRef Partition { get; } = partition;
    internal OutboxStatus Outbox { get; } = outbox;
    internal HashSet<string> ConfiguredResources { get; } = [];
    internal BlobGlobalQuotaR49Upload Control { get; } = new(new(partition, BlobGlobalQuotaR49Corpus.ControlResource, BlobGlobalQuotaR49Corpus.ControlName), BlobGlobalQuotaR49Corpus.SmallLength);
    internal BlobGlobalQuotaR49Upload Held { get; } = new(new(partition, BlobGlobalQuotaR49Corpus.FirstResource, BlobGlobalQuotaR49Corpus.HeldName), BlobGlobalQuotaR49Corpus.FirstReservation);
    internal BlobGlobalQuotaR49Upload Attempt { get; } = new(new(partition, BlobGlobalQuotaR49Corpus.SecondResource, BlobGlobalQuotaR49Corpus.TargetName), BlobGlobalQuotaR49Corpus.SecondReservation);
    internal BlobGlobalQuotaR49Upload? Healthy
    {
        get; set;
    }
    internal BlobGlobalQuotaR49Upload[] CurrentUploads => Healthy is { } healthy ? [Control, Held, Attempt, healthy] : [Control, Held, Attempt];
}
