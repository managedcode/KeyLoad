using KeyLoad.Client;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record KeyLoadTimeSeriesIntensivePeer(int Index, string SiloName, Uri Endpoint, KeyLoadClient Client);
