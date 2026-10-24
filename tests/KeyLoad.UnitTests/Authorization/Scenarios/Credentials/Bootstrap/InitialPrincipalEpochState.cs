using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Authorization;

internal sealed record InitialPrincipalEpochState(Command<DatabaseCommand> Operation, CommitReceipt Receipt, byte[] Outcome);
