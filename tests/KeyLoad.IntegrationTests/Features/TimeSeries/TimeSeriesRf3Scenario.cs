using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

/// <summary>Creates caller-visible TimeSeries state on the actual Docker RF3 cluster.</summary>
internal sealed record TimeSeriesRf3Scenario(PartitionRef Partition)
{
    internal const string Set = "metrics";
    internal const string Series = "cpu";
    internal const string EmptySeries = "empty";
    internal const string OverflowSeries = "overflow";
    internal const string WindowMaximumSeries = "window-maximum";
    internal const string Database = "database";
    internal const string TransactionDomain = "metrics-domain";
    internal const string SecretField = "/secret";
    internal const string SecretClassification = "private";
    internal const string SecretGrant = "time-series.private.read";
    internal const string PrivateTags = "{\"secret\":\"private-marker\",\"visible\":17}";
    internal const string ProjectedTags = "{\"visible\":17}";
    internal const string PublicTags = "{\"visible\":17}";
    internal const string Node1 = "node1";
    internal const string Node2 = "node2";
    internal const string Node3 = "node3";
    internal const int FirstNode = 1;
    internal const int NodeCount = 3;
    internal const int ThirdSequence = 3;
    internal const int OneSampleCap = 1;
    internal const int TwoSampleCap = 2;
    internal const int DefaultSampleLimit = 10_000;
    internal const int DefaultWindowLimit = 1_000;
    internal const double SumTolerance = 0.0000001;
    internal static readonly DateTimeOffset Start = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    internal static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    internal static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    internal static async Task<TimeSeriesRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("series-tenant-" + Guid.NewGuid().ToString("N"), Database,
            TransactionDomain, Guid.NewGuid().ToString("N"));
        using var http = McpCallerHttp.Create(fixture, Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var definition = new ResourceDefinition(Set, ResourceKind.TimeSeries, partition.TransactionDomainId)
        {
            FieldPolicies = [new(SecretField, SecretClassification, SecretGrant)]
        };
        var configured = await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, definition), cancellationToken);
        await McpCallerAssertions.SdkSuccessAsync(configured);
        return new(partition);
    }

    internal async Task AppendAsync(ClusterFixture fixture, string seriesId,
        ImmutableArray<SampleData> samples, string tags, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var command = new CommandRequest(Guid.NewGuid(), Partition,
            [new AppendSamples(Set, seriesId, samples, tags)]);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command, cancellationToken));
    }

    internal static SampleData Data(string id, DateTimeOffset timestamp, double value) => new(id, timestamp, value);

    internal static ImmutableArray<SampleData> Samples(params SampleData[] samples) => [.. samples];

    internal AggregateSamplesRequest Aggregate(DateTimeOffset from, DateTimeOffset? until = null,
        int maxSamples = DefaultSampleLimit) => new(Partition, Set, Series, from, until, maxSamples);

    internal AggregateSampleWindowsRequest Windows(DateTimeOffset from, DateTimeOffset? until,
        TimeSpan width, int maxSamples = DefaultSampleLimit, int maxWindows = DefaultWindowLimit)
        => new(Partition, Set, Series, from, until, width, maxSamples, maxWindows);

    internal ReadLatestSampleRequest Latest(string seriesId = Series, DateTimeOffset? atOrBefore = null)
        => new(Partition, Set, seriesId, atOrBefore);

    internal static SampleAggregate RawOracle(IEnumerable<SampleData> samples)
    {
        var values = samples.Select(sample => sample.Value).ToArray();
        if (values.Length == 0)
        {
            return new(0, 0, null, null, null);
        }

        var sum = values.Sum();
        return new(values.LongLength, sum, values.Min(), values.Max(), sum / values.LongLength);
    }
}
