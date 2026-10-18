using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobRangeRetainedOperation(OperationKind Kind, Guid CommandId,
    string PayloadJson, Result<object> Original, Command<DatabaseCommand> Prepared);
