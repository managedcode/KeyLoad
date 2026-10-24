namespace KeyLoad.UnitTests.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(GraphFixtures.BranchAlias)]
internal sealed record GraphBranches(
    [property: global::Orleans.Id(0)] GraphNode Shallow,
    [property: global::Orleans.Id(1)] GraphNode Deep);
