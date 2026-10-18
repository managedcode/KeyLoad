namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueDeadlineLeaderClusterObservation(string ResourceName, NodeStatus Status, string Voter);
