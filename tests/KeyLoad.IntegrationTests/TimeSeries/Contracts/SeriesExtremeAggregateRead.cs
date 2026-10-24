using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal delegate Task<SampleAggregate> SeriesExtremeAggregateRead(KeyLoadClient sdk, McpOfficialClient mcp,
    AggregateSamplesRequest request, SampleAggregate expected, Func<Task> current, CancellationToken token);
