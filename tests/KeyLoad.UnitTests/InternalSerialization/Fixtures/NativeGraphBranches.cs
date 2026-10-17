namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeGraphFixtures.BranchAlias)]
internal sealed record NativeGraphBranches(
    [property: global::Orleans.Id(0)] NativeGraphNode Shallow,
    [property: global::Orleans.Id(1)] NativeGraphNode Deep);
