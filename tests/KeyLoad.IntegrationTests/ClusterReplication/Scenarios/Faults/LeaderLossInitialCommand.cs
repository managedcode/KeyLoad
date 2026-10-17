namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record LeaderLossInitialCommand(CommandRequest Command, CommitReceipt Receipt);
