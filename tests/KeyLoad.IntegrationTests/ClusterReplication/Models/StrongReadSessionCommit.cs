namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record StrongReadSessionCommit(CommandRequest Command, CommitReceipt Receipt);
