namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Independent immutable seed and expected-value row for a partition leaf.</summary>
internal sealed record PartitionQueryRf3InputRow(
    PartitionRef Partition,
    string Id,
    int Rank,
    string Value);
