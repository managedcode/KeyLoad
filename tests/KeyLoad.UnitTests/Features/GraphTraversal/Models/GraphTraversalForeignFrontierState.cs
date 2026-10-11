namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed record GraphTraversalForeignFrontierState(EntityRef Source, EntityRef Foreign,
    EdgeRecord SourceEdge, (CommandRequest Command, CommitReceipt Receipt)[] History);
