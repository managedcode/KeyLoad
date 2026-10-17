namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkPendingRf3Protocol
{
    internal const int NativeAdmissionCapacity = 32;
    internal const int FirstIndex = 0;
    internal const int AfterFirstSettled = 1;
    internal const int CandidateOrdinal = NativeAdmissionCapacity;
    internal const int FreshOrdinal = NativeAdmissionCapacity + 1;
    internal const string SeriesPrefix = "native-pending-";
    internal const string AppendKind = "appendSamples";
    internal const string IdentityAlias = "keyload.core.sample-chunk-work-hint.v1";
    internal const int GuidBytes = 16;
    internal const string Missing = "The original native pending chunk evidence is unavailable.";
    internal const string RefusalPrefix = "Native chunk admission ";
    internal const string RefusalSuffix = " refused with bounded error code ResourceExhausted.";
    internal const string GuidFormat = "D";
}
