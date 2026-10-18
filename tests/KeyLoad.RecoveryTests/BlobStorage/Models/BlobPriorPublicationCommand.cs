using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobPriorPublicationCommand(Command<DatabaseCommand> Operation, Result<object> Result);
