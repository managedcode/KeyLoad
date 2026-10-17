namespace KeyLoad.Core;

/// <summary>Stores the native-format discriminator and checksummed public snapshot contract.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(AggregateSnapshotPersistence.SnapshotEnvelopeAlias)]
internal sealed record AggregateSnapshotEnvelope(
    [property: Orleans.Id(0)] int FormatVersion,
    [property: Orleans.Id(1)] AggregateSnapshotState Snapshot);
