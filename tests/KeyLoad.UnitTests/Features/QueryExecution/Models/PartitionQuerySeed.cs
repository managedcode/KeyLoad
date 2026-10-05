namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record PartitionQuerySeed(string Id, int Score, string Label, string? Secret = null,
    RowAccess? Access = null);
