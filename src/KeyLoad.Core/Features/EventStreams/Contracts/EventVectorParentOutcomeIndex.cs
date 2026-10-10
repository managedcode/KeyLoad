using System.Collections.Immutable;

namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorParentOutcomeIndex.SerializerAlias)]
internal sealed record EventVectorParentOutcomeIndex(
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field0)] int Version,
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field1)] string PrincipalId,
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field2)] Guid OriginalPublicCommandId,
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field3)] PartitionRef ControlPartition,
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field4)] Guid MapId,
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field5)] ReadOnlyMemory<byte> OriginalPublicRequestDigest,
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field6)] ReadOnlyMemory<byte> OriginalNativeTerminalPhaseBytes,
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field7)] Guid ActualTerminalControlPhaseId,
    [property: Orleans.Id(EventVectorParentOutcomeIndex.Field8)] string ActualTerminalOutcomeFingerprint)
{
    internal const int ScopesField = 9;
    [Orleans.Id(ScopesField)]
    public ImmutableArray<EventVectorAuthorizationScope> OriginalAuthorizedSourceScopes { get; init; }

    internal const string SerializerAlias = "keyload.core.event-vector-parent-outcome-index.v1";
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
