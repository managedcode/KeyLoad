using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.RecoveryTests.Messaging;

internal sealed record LocalInboxProcessingRecoveryState(
    (Command<DatabaseCommand> Operation, Result<object> Result)[] History,
    Command<DatabaseCommand> Receive, Result<object> Received);
