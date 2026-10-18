namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochClusterNodeObservation(string Name, NodeStatus Status, AdminNodeSnapshot Admin);
