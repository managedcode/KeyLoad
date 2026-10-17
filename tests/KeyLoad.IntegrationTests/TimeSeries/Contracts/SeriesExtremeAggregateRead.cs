using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal delegate Task<SampleAggregate> SeriesExtremeAggregateRead(KeyLoadClient sdk, McpOfficialClient mcp,
    AggregateSamplesRequest request, SampleAggregate expected, Func<Task> current, CancellationToken token);
