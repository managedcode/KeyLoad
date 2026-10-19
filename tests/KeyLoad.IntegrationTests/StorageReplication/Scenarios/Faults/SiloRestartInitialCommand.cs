namespace KeyLoad.IntegrationTests.Features.StorageReplication;

internal sealed record SiloRestartInitialCommand(CommandRequest Command, CommitReceipt Receipt);
