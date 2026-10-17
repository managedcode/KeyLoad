namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCleanupPinIdentity.SerializerAlias)]
internal sealed record EventVectorCleanupPinIdentity(
    [property: Orleans.Id(EventVectorCleanupPinIdentity.Field0)] ReadOnlyMemory<byte> OriginalPinPhaseBytes,
    [property: Orleans.Id(EventVectorCleanupPinIdentity.Field1)] ReadOnlyMemory<byte> LastAdmittedAdvancePhaseBytes,
    [property: Orleans.Id(EventVectorCleanupPinIdentity.Field2)] ReadOnlyMemory<byte> CurrentPinRowBytes,
    [property: Orleans.Id(EventVectorCleanupPinIdentity.Field3)] long? CurrentCoverageGeneration,
    [property: Orleans.Id(EventVectorCleanupPinIdentity.Field4)] long? CurrentPinRevision)
{
    internal const string SerializerAlias = "keyload.core.event-vector-cleanup-pin-identity.v1";
    internal const int Field0 = 0;
    internal const int Field1 = 1;
    internal const int Field2 = 2;
    internal const int Field3 = 3;
    internal const int Field4 = 4;
}
