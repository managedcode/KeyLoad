using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateUserAnalyticsProtocol
{
    internal const int Empty = 0;
    internal const int FirstRevision = 1;
    internal const int UpdatedRevision = 2;
    internal const int PageLimit = 3;
    internal const int MaximumGroups = 2;
    internal const string Prefix = "gate_analytics_";
    internal const string Cohort = "cohort";
    internal const string Kind = "kind";
    internal const string Value = "value";
    internal const string Weight = "weight";
    internal const double WeightValue = 3;
    internal const string Alpha = "alpha";
    internal const string Beta = "beta";
    internal const string Count = "count";
    internal const string Sum = "sum";
    internal const string Average = "average";
    internal const string Minimum = "minimum";
    internal const string Maximum = "maximum";
    internal static readonly AnalyticsStoreSchema Schema = new([Cohort, Kind], [Value, Weight]);
    internal static readonly ImmutableArray<AnalyticsMetricSelection> Selections =
    [
        new(Count, string.Empty, AnalyticsAggregateKind.Count),
        new(Sum, Value, AnalyticsAggregateKind.Sum),
        new(Average, Value, AnalyticsAggregateKind.Average),
        new(Minimum, Value, AnalyticsAggregateKind.Min),
        new(Maximum, Value, AnalyticsAggregateKind.Max)
    ];
}
