namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed record GraphTraversalIntegrityState(CommandRequest SeedCommand, CommitReceipt SeedReceipt,
    EdgeRecord HealthyEdge, EdgeRecord DamagedEdge);
