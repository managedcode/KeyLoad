using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal sealed record LocalInboxProcessingRecoveryState(
    (Command<DatabaseCommand> Operation, Result<object> Result)[] History,
    Command<DatabaseCommand> Receive, Result<object> Received);
