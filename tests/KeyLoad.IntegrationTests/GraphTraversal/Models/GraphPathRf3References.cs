namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed record GraphPathRf3References(
    EntityRef Source,
    EntityRef FirstBranch,
    EntityRef SecondBranch,
    EntityRef Target,
    EntityRef HiddenSource,
    EntityRef HiddenIntermediate,
    EntityRef HiddenTarget,
    EntityRef Isolated);
