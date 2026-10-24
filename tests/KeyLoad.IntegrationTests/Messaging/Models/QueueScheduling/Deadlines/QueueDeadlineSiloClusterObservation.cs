namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueDeadlineSiloClusterObservation(string ResourceName, NodeStatus Status,
    string SiloName, string[] ActiveSilos, Guid ProcessInstance);
