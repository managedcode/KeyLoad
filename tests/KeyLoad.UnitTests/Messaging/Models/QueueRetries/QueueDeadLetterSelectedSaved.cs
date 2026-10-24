using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueDeadLetterSelectedSaved(Command<DatabaseCommand> Operation, Result<object> Result);
