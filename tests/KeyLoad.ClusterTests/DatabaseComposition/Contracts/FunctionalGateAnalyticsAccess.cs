using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsAccess
{
    internal const string ReadGrant = "functional-analytics-read";
    internal const string UseGrant = "functional-analytics-use";
    internal const string WriteGrant = "functional-analytics-write";
    private const string MetricPath = "/metrics/value";
    private const string Classification = "private";
    internal static ImmutableArray<string> Grants => [ReadGrant, UseGrant, WriteGrant];
    internal static ImmutableArray<SensitiveFieldPolicy> Policies
        => [new(MetricPath, Classification, ReadGrant, UseGrant, WriteGrant)];
}
