using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeadLetterSelectedSaved(Command<DatabaseCommand> Operation, Result<object> Result);
