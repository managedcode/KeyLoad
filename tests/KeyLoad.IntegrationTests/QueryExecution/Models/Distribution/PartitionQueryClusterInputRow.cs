namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Independent immutable seed and expected-value row for a partition leaf.</summary>
internal sealed record PartitionQueryClusterInputRow(
    PartitionRef Partition,
    string Id,
    int Rank,
    string Value);
