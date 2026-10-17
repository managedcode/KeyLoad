using KeyLoad.Client;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record KeyLoadTimeSeriesIntensivePeer(int Index, string VoterId, Uri Endpoint, KeyLoadClient Client);
