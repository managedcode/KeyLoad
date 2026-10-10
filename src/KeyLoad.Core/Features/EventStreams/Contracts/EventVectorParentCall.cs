using System.Collections.Immutable;

namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorParentCall.SerializerAlias)]
internal sealed record EventVectorParentCall(
    [property: Orleans.Id(EventVectorParentCall.Field0)] int Version,
    [property: Orleans.Id(EventVectorParentCall.Field1)] string PrincipalId,
    [property: Orleans.Id(EventVectorParentCall.Field2)] Guid OriginalPublicCommandId,
    [property: Orleans.Id(EventVectorParentCall.Field3)] PartitionRef ControlPartition,
    [property: Orleans.Id(EventVectorParentCall.Field4)] Guid MapId,
    [property: Orleans.Id(EventVectorParentCall.Field5)] ReadOnlyMemory<byte> OriginalPublicRequestBytes,
    [property: Orleans.Id(EventVectorParentCall.Field6)] ReadOnlyMemory<byte> OriginalPublicRequestDigest,
    [property: Orleans.Id(EventVectorParentCall.Field7)] Guid LastAdmittedControlPhaseId,
    [property: Orleans.Id(EventVectorParentCall.Field8)] ReadOnlyMemory<byte> OriginalAdmittedControlPhaseBytes)
{
    internal const int ExpiryField = 10;
    [Orleans.Id(ExpiryField)]
    public DateTimeOffset OriginalParentExpiresAt { get; init; }

    internal const int ScopesField = 9;
    [Orleans.Id(ScopesField)]
    public ImmutableArray<EventVectorAuthorizationScope> OriginalAuthorizedSourceScopes { get; init; }

    internal const string SerializerAlias = "keyload.core.event-vector-parent-call.v1";
    internal const uint Field0 = 0;
    internal const uint Field1 = 1;
    internal const uint Field2 = 2;
    internal const uint Field3 = 3;
    internal const uint Field4 = 4;
    internal const uint Field5 = 5;
    internal const uint Field6 = 6;
    internal const uint Field7 = 7;
    internal const uint Field8 = 8;
}
