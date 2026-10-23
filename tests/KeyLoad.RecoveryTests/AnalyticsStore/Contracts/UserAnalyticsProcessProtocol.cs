using System.Collections.Immutable;

namespace KeyLoad.RecoveryTests.Features.AnalyticsStore;

internal static class UserAnalyticsProcessProtocol
{
    internal const string Prepare = "user-analytics-derived-prepare";
    internal const string Recover = "user-analytics-derived-recover";
    internal const string PhysicalDirectory = "physical";
    internal const string EvidenceFile = "analytics-original.bin";
    internal const string EvidenceAlias = "keyload.crashhost.analytics-derived-original";
    internal const string Tenant = "analytics-process";
    internal const string Database = "database";
    internal const string Domain = "events";
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
    internal const string Invalid = "The original analytics process recovery invariant failed.";
    internal const int ArgumentCount = 2;
    internal const int RootArgument = 0;
    internal const int ModeArgument = 1;
    internal const int CatalogVersion = 1;
    internal const long Absent = 0;
    internal const long Created = 1;
    internal const long Updated = 2;
    internal const double FirstValue = 10d;
    internal const double SecondValue = 20d;
    internal const double UpdatedValue = 40d;
    private const int BusinessEpochDays = 2;
    private const int MaximumGroups = 10;
    private const long EventCount = 2;
    private const double ExpectedCount = 2d;
    private const double InitialSum = 30d;
    private const double InitialAverage = 15d;
    private const double UpdatedSum = 60d;
    private const double UpdatedAverage = 30d;
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, First);
    internal static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch.AddDays(BusinessEpochDays);

    internal static AnalyticsEventData Event(string id, double amount)
        => new(id, Epoch, ImmutableDictionary<string, string>.Empty.Add(Region, North),
            ImmutableDictionary<string, double>.Empty.Add(Amount, amount), null, []);
    internal static AggregateAnalyticsRequest Query() => new(Partition, Store, Epoch.AddSeconds(-1), Epoch.AddSeconds(1),
        [Region], [new(Count, string.Empty, AnalyticsAggregateKind.Count), new(Sum, Amount, AnalyticsAggregateKind.Sum),
            new(Average, Amount, AnalyticsAggregateKind.Average), new(Minimum, Amount, AnalyticsAggregateKind.Min),
            new(Maximum, Amount, AnalyticsAggregateKind.Max)], MaximumGroups);
    internal static AnalyticsAggregateResult Expected(bool updated)
        => new([new(ImmutableDictionary.Create<string, string>(StringComparer.Ordinal).Add(Region, North),
            ImmutableDictionary.Create<string, double>(StringComparer.Ordinal).Add(Count, ExpectedCount).Add(Sum, updated ? UpdatedSum : InitialSum)
                .Add(Average, updated ? UpdatedAverage : InitialAverage).Add(Minimum, updated ? SecondValue : FirstValue)
                .Add(Maximum, updated ? UpdatedValue : SecondValue))], EventCount);
}
