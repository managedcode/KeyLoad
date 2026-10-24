namespace KeyLoad.UnitTests.QueryExecution;

internal sealed record PartitionQuerySeed(string Id, int Score, string Label, string? Secret = null);
