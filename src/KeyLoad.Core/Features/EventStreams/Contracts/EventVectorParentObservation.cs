namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorParentObservation.SerializerAlias)]
internal sealed record EventVectorParentObservation(
    [property: Orleans.Id(EventVectorParentObservation.Field0)] int Version,
    [property: Orleans.Id(EventVectorParentObservation.Field1)] string PrincipalId,
    [property: Orleans.Id(EventVectorParentObservation.Field2)] Guid OriginalPublicCommandId,
    [property: Orleans.Id(EventVectorParentObservation.Field3)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(EventVectorParentObservation.Field4)] Guid NodeId,
    [property: Orleans.Id(EventVectorParentObservation.Field5)] long ReadGeneration,
    [property: Orleans.Id(EventVectorParentObservation.Field6)] ReadOnlyMemory<byte> OriginalNativeControlPhaseBytes,
    [property: Orleans.Id(EventVectorParentObservation.Field7)] ReadOnlyMemory<byte> OriginalNativeOutcomeBytes)
{
    internal const string SerializerAlias = "keyload.core.event-vector-parent-observation.v1";
    internal const int Field0 = 0;
    internal const int Field1 = 1;
    internal const int Field2 = 2;
    internal const int Field3 = 3;
    internal const int Field4 = 4;
    internal const int Field5 = 5;
    internal const int Field6 = 6;
    internal const int Field7 = 7;
}
