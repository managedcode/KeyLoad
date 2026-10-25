namespace KeyLoad.ClusterTests.StorageRecovery;

internal sealed record OwnedChildProcessResult(int ExitCode, string Stdout, string Stderr);
