using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobPriorPublicationCommand(Command<DatabaseCommand> Operation, Result<object> Result,
    CommitToken ExpectedToken, long PreviousPosition);
