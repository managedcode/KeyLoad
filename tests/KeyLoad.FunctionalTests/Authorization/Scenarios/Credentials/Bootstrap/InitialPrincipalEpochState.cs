using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.Authorization;

internal sealed record InitialPrincipalEpochState(Command<DatabaseCommand> Operation, CommitReceipt Receipt, byte[] Outcome);
