namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCleanupCall.SerializerAlias)]
internal sealed record EventVectorCleanupCall(
    [property: Orleans.Id(EventVectorCleanupCall.Field0)] int Version,
    [property: Orleans.Id(EventVectorCleanupCall.Field1)] Guid MapId,
    [property: Orleans.Id(EventVectorCleanupCall.Field2)] PartitionRef ControlPartition,
    [property: Orleans.Id(EventVectorCleanupCall.Field3)] Guid OriginalParentCommandId,
    [property: Orleans.Id(EventVectorCleanupCall.Field4)] ReadOnlyMemory<byte> OriginalParentRequestDigest,
    [property: Orleans.Id(EventVectorCleanupCall.Field5)] ReadOnlyMemory<byte> OriginalAdmittedPhaseBytes,
    [property: Orleans.Id(EventVectorCleanupCall.Field6)] ReadOnlyMemory<byte> OriginalReleaseRequestBytes,
    [property: Orleans.Id(EventVectorCleanupCall.Field7)] Guid ReleasePublicCommandId,
    [property: Orleans.Id(EventVectorCleanupCall.Field8)] ReadOnlyMemory<byte> ReleaseRequestDigest,
    [property: Orleans.Id(EventVectorCleanupCall.Field9)] DateTimeOffset ReleaseExpiresAt,
    [property: Orleans.Id(EventVectorCleanupCall.Field10)] ReadOnlyMemory<byte> AuthenticatedFrontierBytes,
    [property: Orleans.Id(EventVectorCleanupCall.Field11)] ReadOnlyMemory<byte> FrontierChecksum,
    [property: Orleans.Id(EventVectorCleanupCall.Field12)] Guid LastAdmittedCleanupPhaseId,
    [property: Orleans.Id(EventVectorCleanupCall.Field13)] ReadOnlyMemory<byte> LastAdmittedCleanupPhaseBytes,
    [property: Orleans.Id(EventVectorCleanupCall.Field14)] System.Collections.Immutable.ImmutableArray<EventVectorAuthorizationScope> OriginalAuthorizedSourceScopes,
    [property: Orleans.Id(EventVectorCleanupCall.Field15)] long CleanupGeneration)
{
    internal const string SerializerAlias = "keyload.core.event-vector-cleanup-call.v1";
    internal const int Field0 = 0;
    internal const int Field1 = 1;
    internal const int Field2 = 2;
    internal const int Field3 = 3;
    internal const int Field4 = 4;
    internal const int Field5 = 5;
    internal const int Field6 = 6;
    internal const int Field7 = 7;
    internal const int Field8 = 8;
    internal const int Field9 = 9;
    internal const int Field10 = 10;
    internal const int Field11 = 11;
    internal const int Field12 = 12;
    internal const int Field13 = 13;
    internal const int Field14 = 14;
    internal const int Field15 = 15;
}
