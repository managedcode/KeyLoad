using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.Analytics;

/// <summary>The only operations an analytics database implements; aggregation runs in the database, never in the client.</summary>
internal interface IAnalyticsDriver : IAsyncDisposable
{
    /// <summary>Events per <see cref="InsertAsync"/> call during the load.</summary>
    int BatchSize { get; }

    /// <summary>Creates the events table or store with dimensions region, product, channel and metrics amount, quantity.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Inserts events; returns after the database acknowledged them.</summary>
    Task InsertAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken token);

    /// <summary><c>count(*), sum(amount) GROUP BY region</c> over <c>[from, until)</c>.</summary>
    Task<IReadOnlyList<GroupTotal>> SumByRegionAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken token);

    /// <summary><c>count(*), sum(amount) WHERE product = @product</c> over <c>[from, until)</c>; the key is the product.</summary>
    Task<GroupTotal> SumForProductAsync(string product, DateTimeOffset from, DateTimeOffset until, CancellationToken token);
}

/// <summary>Creates the analytics driver of the deployed database.</summary>
internal static class AnalyticsDrivers
{
    internal static IAnalyticsDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadAnalyticsDriver(connection),
        BenchmarkDatabases.ClickHouse => new ClickHouseAnalyticsDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.Analytics + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
