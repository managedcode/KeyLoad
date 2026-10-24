namespace KeyLoad.IntegrationTests.StorageReplication;

internal sealed record SiloRestartInitialCommand(CommandRequest Command, CommitReceipt Receipt);
