namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueDeadlineLeaderRf3Observation(string ResourceName, NodeStatus Status, string Voter);
