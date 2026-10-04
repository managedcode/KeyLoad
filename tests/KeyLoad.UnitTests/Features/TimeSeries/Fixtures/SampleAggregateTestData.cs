using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleAggregateTestData
{
    internal const string RootPrincipal = "root";
    internal const string ReaderPrincipal = "series-reader";
    internal const string RevokedPrincipal = "revoked-reader";
    internal const string Set = "metrics";
    internal const string Series = "cpu";
    internal const string EmptySeries = "empty";
    internal const string ProtectedPath = "/secret";
    internal const string PrivateClassification = "private";
    internal const string PrivateGrant = "time-series.private.read";
    internal const string ProtectedTags = "{\"secret\":\"sensitive\",\"visible\":17}";
    internal const string ExpectedProjectedTags = "{\"visible\":17}";
    internal const string EmptyTags = "{}";
    internal const string EventPrefix = "sample-";
    internal const string SensitiveValue = "sensitive";
    internal const string UnexpectedErrorCodeMessage = "The operation returned an unexpected error code.";
    internal const string PrivatePrincipalSuffix = "-private";
    internal const int RawReadBudgetBytes = 4_096;
    internal const int SmallHistoryCount = 128;
    internal const int ExactWindowCount = 3;
    internal const int NoResults = 0;
    internal const int OneResult = 1;
    internal const int ZeroScanRecords = 0;
    internal const int ZeroResults = 0;
    internal const int DeadlineSeconds = 0;
    internal const int TinyReadBytes = 1;
    internal const int ServerWindowLimit = 3;
    internal const int ExactBatchExtraByte = 1;
    internal static readonly DateTimeOffset Start = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    internal static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan ThreeMinutes = TimeSpan.FromMinutes(3);
    internal static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    internal static void Configure(TestDatabase database, SensitiveFieldPolicy[]? fields = null)
        => database.Configure(Set, ResourceKind.TimeSeries, fields: fields);

    internal static ImmutableArray<SampleData> Samples(params SampleData[] samples) => [.. samples];

    internal static SampleData Data(int index, DateTimeOffset timestamp, double value)
        => new(EventPrefix + index, timestamp, value);

    internal static CommitReceipt Append(TestDatabase database, params SampleData[] samples)
        => database.Commit(new AppendSamples(Set, Series, [.. samples], EmptyTags));

    internal static CommitReceipt Append(TestDatabase database, ImmutableArray<SampleData> samples, string tags)
        => database.Commit(new AppendSamples(Set, Series, samples, tags));

    internal static SampleAggregate Oracle(IEnumerable<SampleRecord> records)
    {
        var count = 0L;
        var sum = 0d;
        double? minimum = null;
        double? maximum = null;
        foreach (var record in records)
        {
            count = checked(count + 1);
            sum += record.Sample.Value;
            minimum = minimum is null || record.Sample.Value < minimum ? record.Sample.Value : minimum;
            maximum = maximum is null || record.Sample.Value > maximum ? record.Sample.Value : maximum;
        }

        return new(count, sum, minimum, maximum, count == 0 ? null : sum / count);
    }

    internal static void GrantRead(TestDatabase database, string principalId, bool includePrivate = false)
    {
        var grants = includePrivate
            ? ImmutableArray.Create(new ScopeGrant(database.Partition.DatabaseId, Set, Capability.SeriesRead))
            : [new ScopeGrant(database.Partition.DatabaseId, Set, Capability.SeriesRead)];
        var fields = includePrivate ? ImmutableArray.Create(PrivateGrant) : ImmutableArray<string>.Empty;
        database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(new(principalId, database.Partition.TenantId, grants, fields)));
    }

    internal static KeyLoadException Failure(Action action, ErrorCode expected)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(action);
        if (failure.Code != expected)
        {
            throw new InvalidOperationException(UnexpectedErrorCodeMessage, failure);
        }
        return failure;
    }
}
