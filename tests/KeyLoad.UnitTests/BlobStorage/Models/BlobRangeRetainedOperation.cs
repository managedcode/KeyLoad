using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobRangeRetainedOperation(OperationKind Kind, Guid CommandId,
    string PayloadJson, Result<object> Original);
