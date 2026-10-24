namespace KeyLoad.IntegrationTests.GraphTraversal;

internal sealed record GraphPathClusterReferences(
    EntityRef Source,
    EntityRef FirstBranch,
    EntityRef SecondBranch,
    EntityRef Target,
    EntityRef HiddenSource,
    EntityRef HiddenIntermediate,
    EntityRef HiddenTarget,
    EntityRef Isolated);
