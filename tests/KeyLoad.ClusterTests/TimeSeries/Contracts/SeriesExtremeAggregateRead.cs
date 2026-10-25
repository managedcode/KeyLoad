using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

internal delegate Task<SampleAggregate> SeriesExtremeAggregateRead(KeyLoadClient sdk, McpOfficialClient mcp,
    AggregateSamplesRequest request, SampleAggregate expected, Func<Task> current, CancellationToken token);
