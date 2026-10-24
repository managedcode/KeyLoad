using KeyLoad.Client;

namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record KeyLoadTimeSeriesIntensivePeer(int Index, string SiloName, Uri Endpoint, KeyLoadClient Client);
