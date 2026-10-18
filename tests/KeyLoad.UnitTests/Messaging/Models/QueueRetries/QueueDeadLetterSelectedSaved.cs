using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeadLetterSelectedSaved(Command<DatabaseCommand> Operation, Result<object> Result);
