using System.Collections.Immutable;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Analytics;

/// <summary>
/// KeyLoad analytics store through the SDK: a <see cref="ResourceKind.AnalyticsStore"/> resource with the
/// <see cref="AnalyticsStoreSchema"/> dimensions and metrics, one <see cref="UpsertAnalyticsEvents"/> commit per batch with
/// expected revision zero for every new event, and server-side <c>AggregateAnalyticsAsync</c> Count and Sum selections.
/// </summary>
/// <remarks>
/// <see cref="AggregateAnalyticsRequest"/> has no filter, so the product total groups by product on the server and
/// returns that product's group.
/// </remarks>
internal sealed class KeyLoadAnalyticsDriver(BenchmarkConnection connection) : IAnalyticsDriver
{
    private const int Batch = 1_000;
    private const long NewEvent = 0;
    private const string Store = "events";
    private const string Channel = "channel", Product = "product", Region = "region";
    private const string Amount = "amount", Quantity = "quantity";
    private const string CountName = "n", SumName = "total";
    private static readonly AnalyticsStoreSchema Schema = new([Channel, Product, Region], [Amount, Quantity]);
    private static readonly ImmutableArray<AnalyticsMetricSelection> Totals =
    [
        new(CountName, string.Empty, AnalyticsAggregateKind.Count),
        new(SumName, Amount, AnalyticsAggregateKind.Sum)
    ];
    private readonly KeyLoadDatabase database = new(connection);

    public int BatchSize => Batch;

    public Task PrepareAsync(CancellationToken token)
        => database.ConfigureAsync(database.Resource(Store, ResourceKind.AnalyticsStore) with { AnalyticsSchema = Schema }, token);

    public async Task InsertAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken token)
    {
        ImmutableArray<AnalyticsEventData> data = [.. events.Select(Event)];
        var revisions = events.ToImmutableDictionary(item => item.Id, _ => NewEvent, StringComparer.Ordinal);
        _ = await database.CommitAsync([new UpsertAnalyticsEvents(Store, data, revisions)], token);
    }

    public async Task<IReadOnlyList<GroupTotal>> SumByRegionAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken token)
    {
        var result = await AggregateAsync(Region, AnalyticsDataset.Regions, from, until, token);
        return [.. result.Rows.Select(row => Total(row, Region))];
    }

    /// <summary>A product without events in the range has no group, which is a zero total.</summary>
    public async Task<GroupTotal> SumForProductAsync(string product, DateTimeOffset from, DateTimeOffset until, CancellationToken token)
    {
        var result = await AggregateAsync(Product, AnalyticsDataset.Products, from, until, token);
        var row = result.Rows.FirstOrDefault(found => string.Equals(found.Dimensions[Product], product, StringComparison.Ordinal));
        return row is null ? new(product, 0, 0) : Total(row, Product);
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();

    private async Task<AnalyticsAggregateResult> AggregateAsync(string dimension, int groups, DateTimeOffset from,
        DateTimeOffset until, CancellationToken token)
    {
        var request = new AggregateAnalyticsRequest(database.Partition, Store, from.ToUniversalTime(), until.ToUniversalTime(),
            [dimension], Totals, groups);
        return KeyLoadDatabase.Require(await database.Client.AggregateAnalyticsAsync(request, token), nameof(AggregateAsync));
    }

    private static AnalyticsEventData Event(AnalyticsEvent item)
    {
        var dimensions = ImmutableDictionary<string, string>.Empty
            .Add(Channel, item.Channel).Add(Product, item.Product).Add(Region, item.Region);
        var metrics = ImmutableDictionary<string, double>.Empty.Add(Amount, item.Amount).Add(Quantity, item.Quantity);
        return new(item.Id, item.At.ToUniversalTime(), dimensions, metrics, null, []);
    }

    private static GroupTotal Total(AnalyticsAggregateRow row, string dimension)
        => new(row.Dimensions[dimension], checked((long)row.Metrics[CountName]), row.Metrics[SumName]);
}
