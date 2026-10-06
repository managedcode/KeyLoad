using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimeSeriesComparisonRecorder(TimeSeriesComparisonWorkload workload, TimeProvider timeProvider)
{
    private const string SampleValueFormat = "R";

    private const string LibraryName = "ManagedCode.TimeSeries";
    private const string LibraryStorage = "in-memory bucket aggregation";
    private const string LibraryPersistence = "none; no persistence, recovery, or replication";
    private const string LibraryAcknowledgement = "AddNewData returns after an in-process bucket update";
    private static readonly string LibraryVersion = TimeSeriesComparisonPackageVersion.Read();
    private const int ReportSchemaVersion = 1;
    private readonly List<TimeSeriesTargetMetadata> targets = [];
    private readonly List<TimeSeriesComparisonAttempt> attempts = [];
    private readonly List<TimeSeriesCorrectnessCheck> checks = [];

    internal void AddTarget(TimeSeriesTargetMetadata metadata) => targets.Add(metadata);

    internal void AddLibraryTarget()
        => targets.Add(new(LibraryName, LibraryStorage, LibraryPersistence, LibraryAcknowledgement, null, LibraryVersion));

    internal void RecordSetup(string target, string operation, bool succeeded, string? errorCode = null)
    {
        const int SingleItemCount = 1;
        const int NoObservedItems = 0;

        attempts.Add(new(target, operation, SingleItemCount, false, null, succeeded, null, errorCode));
        checks.Add(new(target, operation, succeeded, SingleItemCount, succeeded ? SingleItemCount : NoObservedItems, HashCount(SingleItemCount), HashCount(succeeded ? SingleItemCount : NoObservedItems), errorCode));
    }

    internal void RecordRead(string target, TimeSeriesReadRange range, TimeSeriesReadResult result, double? elapsedMs)
    {
        const int UpdateIdentityBlock = 1;
        const int NoItems = 0;

        var expected = workload.Samples.Where(sample => InRange(sample, range)).ToImmutableArray();
        var expectedHash = HashSamples(expected);
        var actualHash = result.Succeeded ? HashSamples(result.Samples) : null;
        var passed = range.ExpectedErrorCode is null
            ? result.Succeeded && result.Samples.Length == range.ExpectedCount && expectedHash == actualHash
            : !result.Succeeded && result.ErrorCode == range.ExpectedErrorCode;
        var attemptSuccess = passed;
        attempts.Add(new(target, range.Name, UpdateIdentityBlock, elapsedMs.HasValue, elapsedMs, attemptSuccess,
            result.Succeeded ? result.Samples.Length : NoItems, result.ErrorCode));
        checks.Add(new(target, range.Name, passed, range.ExpectedCount,
            result.Succeeded ? result.Samples.Length : NoItems, expectedHash, actualHash, result.ErrorCode));
    }

    internal void RecordTargetFailure(string target, string operation, string errorCode, double? elapsedMs = null)
    {
        const int SingleItemCount = 1;
        const int NoObservedItems = 0;

        attempts.Add(new(target, operation, SingleItemCount, elapsedMs.HasValue, elapsedMs, false, null, errorCode));
        checks.Add(new(target, operation, false, SingleItemCount, NoObservedItems, HashCount(SingleItemCount), HashCount(NoObservedItems), errorCode));
    }

    internal void RecordBuckets(string target, ImmutableArray<TimeSeriesBucketValue> actual, double elapsedMs)
    {
        const string BucketSumOracleToken = "bucket-sum-oracle";
        const int SingleItemCount = 1;

        var expectedHash = HashBuckets(workload.ExpectedBuckets);
        var actualHash = HashBuckets(actual);
        var passed = workload.ExpectedBuckets.Length == actual.Length && expectedHash == actualHash;
        attempts.Add(new(target, BucketSumOracleToken, SingleItemCount, true, elapsedMs, passed, actual.Length, null));
        checks.Add(new(target, BucketSumOracleToken, passed, workload.ExpectedBuckets.Length,
            actual.Length, expectedHash, actualHash, null));
    }

    internal void RecordCleanup(string target, bool succeeded, string? errorCode = null)
    {
        const string CleanupToken = "cleanup";
        const int SingleItemCount = 1;
        const int NoObservedItems = 0;

        attempts.Add(new(target, CleanupToken, SingleItemCount, false, null, succeeded, null, errorCode));
        checks.Add(new(target, CleanupToken, succeeded, SingleItemCount, succeeded ? SingleItemCount : NoObservedItems,
            HashCount(SingleItemCount), HashCount(succeeded ? SingleItemCount : NoObservedItems), errorCode));
    }

    internal TimeSeriesComparisonReport CreateReport(string sourceRevision)
        => new(ReportSchemaVersion, sourceRevision, workload.RunId, timeProvider.GetUtcNow(),
            workload.ContentHash, workload.Samples.Length, workload.BucketWidth, [.. targets],
            [.. attempts], [.. checks]);

    internal bool Passed => checks.All(check => check.Passed) && attempts.All(attempt => attempt.Succeeded);

    private static bool InRange(TimeSeriesSamplePoint sample, TimeSeriesReadRange range)
        => sample.Timestamp.UtcTicks >= range.From.UtcTicks && sample.Timestamp.UtcTicks <= range.Until.UtcTicks;

    private static string HashSamples(IEnumerable<TimeSeriesSamplePoint> samples)
    {
        const char ComponentSeparator = '|';
        const char LineFeed = '\n';

        var content = new StringBuilder();
        foreach (var sample in samples.OrderBy(item => item.Timestamp.UtcTicks).ThenBy(item => item.EventId, StringComparer.Ordinal))
        {
            content.Append(sample.EventId).Append(ComponentSeparator)
                .Append(sample.Timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append(ComponentSeparator)
                .Append(sample.Value.ToString(SampleValueFormat, CultureInfo.InvariantCulture)).Append(ComponentSeparator)
                .Append(TimeSeriesJsonCanonicalizer.Canonicalize(sample.TagsJson)).Append(LineFeed);
        }

        return Hash(content);
    }

    private static string HashBuckets(IEnumerable<TimeSeriesBucketValue> buckets)
    {
        const char ComponentSeparator = '|';
        const char LineFeed = '\n';

        var content = new StringBuilder();
        foreach (var bucket in buckets.OrderBy(item => item.TimestampUtc.UtcTicks))
        {
            content.Append(bucket.TimestampUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append(ComponentSeparator)
                .Append(bucket.Sum.ToString(SampleValueFormat, CultureInfo.InvariantCulture)).Append(LineFeed);
        }

        return Hash(content);
    }

    private static string HashCount(int count) => HashCount(count.ToString(CultureInfo.InvariantCulture));

    private static string HashCount(string count) => Hash(count);

    private static string Hash(StringBuilder content) => Hash(content.ToString());

    private static string Hash(string content)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
