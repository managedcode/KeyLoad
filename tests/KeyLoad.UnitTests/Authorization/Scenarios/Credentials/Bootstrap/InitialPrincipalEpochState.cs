namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record InitialPrincipalEpochState(ReplicatedOperation Operation, CommitReceipt Receipt, byte[] Outcome);
