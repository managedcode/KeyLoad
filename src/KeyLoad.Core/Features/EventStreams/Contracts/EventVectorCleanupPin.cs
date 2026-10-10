namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCleanupPin.SerializerAlias)]
internal sealed record EventVectorCleanupPin(
    [property: Orleans.Id(EventVectorCleanupPin.Field0)] int Version,
    [property: Orleans.Id(EventVectorCleanupPin.Field1)] ReadOnlyMemory<byte> OriginalPinPhaseBytes,
    [property: Orleans.Id(EventVectorCleanupPin.Field2)] ReadOnlyMemory<byte> LastAdmittedAdvancePhaseBytes,
    [property: Orleans.Id(EventVectorCleanupPin.Field3)] ReadOnlyMemory<byte> AuthenticatedSourceObservationBytes,
    [property: Orleans.Id(EventVectorCleanupPin.Field4)] long? CurrentCoverageGeneration,
    [property: Orleans.Id(EventVectorCleanupPin.Field5)] long? CurrentPinRevision)
{
    internal const string SerializerAlias = "keyload.core.event-vector-cleanup-pin.v1";
    internal const int Field0 = 0;
    internal const int Field1 = 1;
    internal const int Field2 = 2;
    internal const int Field3 = 3;
    internal const int Field4 = 4;
    internal const int Field5 = 5;
}
