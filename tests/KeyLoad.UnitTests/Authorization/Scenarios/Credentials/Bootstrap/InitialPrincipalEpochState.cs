using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record InitialPrincipalEpochState(Command<DatabaseCommand> Operation, CommitReceipt Receipt, byte[] Outcome);
