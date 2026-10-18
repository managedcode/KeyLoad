namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record SiloRestartInitialCommand(CommandRequest Command, CommitReceipt Receipt);
