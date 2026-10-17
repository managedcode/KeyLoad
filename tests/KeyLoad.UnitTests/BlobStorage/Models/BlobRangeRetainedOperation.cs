namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record BlobRangeRetainedOperation(OperationKind Kind, Guid CommandId,
    string PayloadJson, OperationResult Original);
