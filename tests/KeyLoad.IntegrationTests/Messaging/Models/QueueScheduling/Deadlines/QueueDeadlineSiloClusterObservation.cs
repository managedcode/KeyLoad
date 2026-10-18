namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueDeadlineSiloClusterObservation(string ResourceName, NodeStatus Status,
    string SiloName, string[] ActiveSilos, Guid ProcessInstance);
