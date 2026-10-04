namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class KeyLoadTimeSeriesIntensiveProtocol
{
    internal const string AppendSamplesKind = "appendSamples";
    internal const string ConfigurePurpose = "configure-resource";
    internal const string SeedPurposePrefix = "seed:";
    internal const string InvalidContext = nameof(KeyLoadTimeSeriesIntensiveProtocol) + ".InvalidContext";
    internal const string InvalidTopology = nameof(KeyLoadTimeSeriesIntensiveProtocol) + ".InvalidTopology";
    internal const string InvalidReceipt = nameof(KeyLoadTimeSeriesIntensiveProtocol) + ".InvalidReceipt";
    internal const string InvalidResult = nameof(KeyLoadTimeSeriesIntensiveProtocol) + ".InvalidResult";
    internal const string ClosedTarget = nameof(KeyLoadTimeSeriesIntensiveProtocol) + ".ClosedTarget";
    internal const long OwnershipEpoch = 1;
    internal const long MinimumPositiveReceiptValue = 1;
    internal const int MinimumPositiveHttpStatus = 1;
    internal const int FirstPeerIndex = 1;
    internal const int MeasurementIngressIndex = 0;
    internal const int FirstStatusIndex = 0;
    internal const int FirstSeedOrdinal = 0;
    internal const int SeedOrdinalOffset = 1;
    internal const int ClosedState = 1;
    internal const int OpenState = 0;
    internal const int SeedBatchSize = 256;
    internal const int SeedBatchCount = 16;
    internal const int MinimumNodeCount = 1;
    internal const int MaximumNodeCount = 3;
}
