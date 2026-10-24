namespace KeyLoad.IntegrationTests.StorageRecovery;

internal sealed record OwnedChildProcessResult(int ExitCode, string Stdout, string Stderr);
