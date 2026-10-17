namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed record RestoreIdentityOverrideCurrent(
    ReplicatedOperation Operation, OperationResult Result, OperationResult Fence, byte[] Outcome, long Position, string[] Image);
