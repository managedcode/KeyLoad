using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed record LocalInboxProcessingRecoveryState(
    (Command<DatabaseCommand> Operation, Result<object> Result)[] History,
    Command<DatabaseCommand> Receive, Result<object> Received);
