namespace KeyLoad.Server.Features.BlobStorage;

internal enum NativeControlledBlobFaultVariation
{
    Nonce = 0,
    RequestId = 1,
    SourcePhysicalShard = 2,
    DestinationIncarnation = 3,
    MissingEnvelope = 4,
    PrincipalPolicyEpoch = 5,
    QueryId = 6,
    Resource = 7,
    ExpiresAt = 8,
    Purpose = 9,
    NativeRequest = 10,
    OriginalOutcome = 11,
    OriginalOperationId = 12
}
