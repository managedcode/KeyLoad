using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimeSeriesComparisonRecorder(TimeSeriesComparisonWorkload workload, TimeProvider timeProvider)
{
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
        attempts.Add(new(target, operation, 1, false, null, succeeded, null, errorCode));
        checks.Add(new(target, operation, succeeded, 1, succeeded ? 1 : 0, HashCount(1), HashCount(succeeded ? 1 : 0), errorCode));
    }

    internal void RecordRead(string target, TimeSeriesReadRange range, TimeSeriesReadResult result, double? elapsedMs)
    {
        var expected = workload.Samples.Where(sample => InRange(sample, range)).ToImmutableArray();
        var expectedHash = HashSamples(expected);
        var actualHash = result.Succeeded ? HashSamples(result.Samples) : null;
        var passed = range.ExpectedErrorCode is null
            ? result.Succeeded && result.Samples.Length == range.ExpectedCount && expectedHash == actualHash
            : !result.Succeeded && result.ErrorCode == range.ExpectedErrorCode;
        var attemptSuccess = passed;
        attempts.Add(new(target, range.Name, 1, elapsedMs.HasValue, elapsedMs, attemptSuccess,
            result.Succeeded ? result.Samples.Length : 0, result.ErrorCode));
        checks.Add(new(target, range.Name, passed, range.ExpectedCount,
            result.Succeeded ? result.Samples.Length : 0, expectedHash, actualHash, result.ErrorCode));
    }

    internal void RecordTargetFailure(string target, string operation, string errorCode, double? elapsedMs = null)
    {
        attempts.Add(new(target, operation, 1, elapsedMs.HasValue, elapsedMs, false, null, errorCode));
        checks.Add(new(target, operation, false, 1, 0, HashCount(1), HashCount(0), errorCode));
    }

    internal void RecordBuckets(string target, ImmutableArray<TimeSeriesBucketValue> actual, double elapsedMs)
    {
        var expectedHash = HashBuckets(workload.ExpectedBuckets);
        var actualHash = HashBuckets(actual);
        var passed = workload.ExpectedBuckets.Length == actual.Length && expectedHash == actualHash;
        attempts.Add(new(target, "bucket-sum-oracle", 1, true, elapsedMs, passed, actual.Length, null));
        checks.Add(new(target, "bucket-sum-oracle", passed, workload.ExpectedBuckets.Length,
            actual.Length, expectedHash, actualHash, null));
    }

    internal void RecordCleanup(string target, bool succeeded, string? errorCode = null)
    {
        attempts.Add(new(target, "cleanup", 1, false, null, succeeded, null, errorCode));
        checks.Add(new(target, "cleanup", succeeded, 1, succeeded ? 1 : 0,
            HashCount(1), HashCount(succeeded ? 1 : 0), errorCode));
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
        var content = new StringBuilder();
        foreach (var sample in samples.OrderBy(item => item.Timestamp.UtcTicks).ThenBy(item => item.EventId, StringComparer.Ordinal))
        {
            content.Append(sample.EventId).Append('|')
                .Append(sample.Timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(sample.Value.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(TimeSeriesJsonCanonicalizer.Canonicalize(sample.TagsJson)).Append('\n');
        }

        return Hash(content);
    }

    private static string HashBuckets(IEnumerable<TimeSeriesBucketValue> buckets)
    {
        var content = new StringBuilder();
        foreach (var bucket in buckets.OrderBy(item => item.TimestampUtc.UtcTicks))
        {
            content.Append(bucket.TimestampUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(bucket.Sum.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        return Hash(content);
    }

    private static string HashCount(int count) => HashCount(count.ToString(CultureInfo.InvariantCulture));

    private static string HashCount(string count) => Hash(count);

    private static string Hash(StringBuilder content) => Hash(content.ToString());

    private static string Hash(string content)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
