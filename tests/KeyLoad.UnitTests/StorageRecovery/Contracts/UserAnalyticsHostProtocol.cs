using System.Collections.Immutable;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class UserAnalyticsHostProtocol
{
    internal const string Tenant = "user-analytics-host";
    internal const string Database = "analytics-lifecycle";
    internal const string Domain = "events";
    internal const string Key = "one";
    internal const string Store = "analytics";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Region = "region";
    internal const string Amount = "amount";
    internal const string North = "north";
    internal const string Count = "count";
    internal const string Sum = "sum";
    internal const string Average = "average";
    internal const string Minimum = "minimum";
    internal const string Maximum = "maximum";
    internal const string DirectoryName = "derived-user-analytics";
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, Key);
    internal static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch.AddDays(2);
    internal static AnalyticsEventData Event(string id, double amount)
        => new(id, Epoch, ImmutableDictionary<string, string>.Empty.Add(Region, North),
            ImmutableDictionary<string, double>.Empty.Add(Amount, amount), null, []);
    internal static AggregateAnalyticsRequest Query() => new(Partition, Store, Epoch.AddSeconds(-1), Epoch.AddSeconds(1),
        [Region], [new(Count, string.Empty, AnalyticsAggregateKind.Count), new(Sum, Amount, AnalyticsAggregateKind.Sum),
         new(Average, Amount, AnalyticsAggregateKind.Average), new(Minimum, Amount, AnalyticsAggregateKind.Min),
         new(Maximum, Amount, AnalyticsAggregateKind.Max)], 10);
    internal static AnalyticsAggregateResult Expected(bool updated)
    {
        var values = ImmutableDictionary.Create<string, double>(StringComparer.Ordinal).Add(Count, updated ? 1d : 2d)
            .Add(Sum, updated ? 40d : 30d).Add(Average, updated ? 40d : 15d)
            .Add(Minimum, updated ? 40d : 10d).Add(Maximum, updated ? 40d : 20d);
        return new([new(ImmutableDictionary.Create<string, string>(StringComparer.Ordinal).Add(Region, North), values)], updated ? 1L : 2L);
    }
}
