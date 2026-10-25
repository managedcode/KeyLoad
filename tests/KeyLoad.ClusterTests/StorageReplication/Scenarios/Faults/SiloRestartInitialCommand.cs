namespace KeyLoad.ClusterTests.StorageReplication;

internal sealed record SiloRestartInitialCommand(CommandRequest Command, CommitReceipt Receipt);
