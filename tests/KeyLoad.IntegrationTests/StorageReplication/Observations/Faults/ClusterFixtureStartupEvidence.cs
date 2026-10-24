using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal sealed class ClusterFixtureStartupEvidence : IDisposable
{
    private const string MissingSession = "The owned Aspire startup profiling session is missing.";
    private const string NoFailure = "Aspire startup first failure: unavailable.";
    private const string SnapshotFormat = "Aspire startup: observed={0}; ensure={1}; api={2}; errors={3}; timeouts={4}; "
        + "configKnown={5}; configExists={6}; lockMs={7}; fileMs={8}; readMs={9}; buildMs={10}; retries={11}; phases={12}.";
    private static readonly CompositeFormat SnapshotTemplate = CompositeFormat.Parse(SnapshotFormat);
    private const int FilePhase = 2;
    private const int ReadPhase = 4;
    private const int ClientPhase = 8;
    private const int ReadyPhase = 16;
    private const int TimeoutPhase = 32;
    private readonly Lock gate = new();
    private readonly string sessionId;
    private readonly ActivityListener listener;
    private bool disposed;
    private bool observed;
    private bool configKnown;
    private bool configExists;
    private long ensureCount;
    private long apiCount;
    private long errorCount;
    private long timeoutCount;
    private long lockMs = -1;
    private long fileMs = -1;
    private long readMs = -1;
    private long buildMs = -1;
    private long retries = -1;
    private int phases;
    private string? firstFailure;

    /// <summary>Observes only execution orchestration activities belonging to this actual AppHost.</summary>
    /// <param name="configuration">The AppHost's private, fixture-owned profiling configuration.</param>
    public ClusterFixtureStartupEvidence(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        sessionId = configuration[ClusterFixtureStartupProfile.SessionSetting]
            ?? throw new InvalidOperationException(MissingSession);
        listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == ClusterFixtureStartupProfile.ActivitySource,
            Sample = static (ref ActivityCreationOptions<ActivityContext> options) => SelectActivityData(options.Name),
            SampleUsingParentId = static (ref ActivityCreationOptions<string> options) => SelectActivityData(options.Name),
            ActivityStopped = Record
        };
        try
        {
            ActivitySource.AddActivityListener(listener);
        }
        catch (Exception)
        {
            listener.Dispose();
            throw;
        }
    }

    /// <summary>Saves two closed, bounded projections beside the original fixture's diagnostics.</summary>
    /// <param name="output">The already admitted and created original results directory.</param>
    internal void Save(string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        string[] lines;
        lock (gate)
        {
            lines = [firstFailure ?? NoFailure, Snapshot()];
        }
        File.WriteAllLines(Path.Combine(output, ClusterFixtureStartupProfile.EvidenceFile), lines);
    }

    /// <summary>Detaches the synchronous observer when the actual AppHost is disposed.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
        }
        listener.Dispose();
    }

    private static ActivitySamplingResult SelectActivityData(string name)
        => name is ClusterFixtureStartupProfile.EnsureOperation or ClusterFixtureStartupProfile.ApiOperation
            ? ActivitySamplingResult.AllData : ActivitySamplingResult.None;

    private void Record(Activity activity)
    {
        if (activity.OperationName is not (ClusterFixtureStartupProfile.EnsureOperation or ClusterFixtureStartupProfile.ApiOperation)
            || activity.GetTagItem(ClusterFixtureStartupProfile.SessionTag) is not string originalSession
            || !string.Equals(originalSession, sessionId, StringComparison.Ordinal))
        {
            return;
        }
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            observed = true;
            if (activity.OperationName == ClusterFixtureStartupProfile.EnsureOperation)
            {
                ensureCount = Increment(ensureCount);
            }
            else
            {
                apiCount = Increment(apiCount);
            }
            var failed = activity.Status == ActivityStatusCode.Error;
            if (failed)
            {
                errorCount = Increment(errorCount);
            }
            CaptureMetrics(activity);
            failed |= CaptureEvents(activity);
            if (failed && firstFailure is null)
            {
                firstFailure = Snapshot();
            }
        }
    }

    private void CaptureMetrics(Activity activity)
    {
        if (activity.GetTagItem(ClusterFixtureStartupProfile.ConfigExistsTag) is bool exists)
        {
            configKnown = true;
            configExists = exists;
        }
        ReadMetric(activity, ClusterFixtureStartupProfile.LockWaitTag, ref lockMs);
        ReadMetric(activity, ClusterFixtureStartupProfile.FileWaitTag, ref fileMs);
        ReadMetric(activity, ClusterFixtureStartupProfile.ReadDurationTag, ref readMs);
        ReadMetric(activity, ClusterFixtureStartupProfile.BuildDurationTag, ref buildMs);
        ReadMetric(activity, ClusterFixtureStartupProfile.ApiRetryTag, ref retries);
    }

    private bool CaptureEvents(Activity activity)
    {
        var timedOut = false;
        foreach (var entry in activity.Events)
        {
            phases |= entry.Name switch
            {
                ClusterFixtureStartupProfile.LockEvent => 1,
                ClusterFixtureStartupProfile.FileEvent => FilePhase,
                ClusterFixtureStartupProfile.ReadEvent => ReadPhase,
                ClusterFixtureStartupProfile.ClientEvent => ClientPhase,
                ClusterFixtureStartupProfile.ReadyEvent => ReadyPhase,
                ClusterFixtureStartupProfile.TimeoutEvent => TimeoutPhase,
                _ => 0
            };
            if (entry.Name == ClusterFixtureStartupProfile.TimeoutEvent)
            {
                timeoutCount = Increment(timeoutCount);
                timedOut = true;
            }
        }
        return timedOut;
    }

    private static void ReadMetric(Activity activity, string key, ref long destination)
    {
        var value = activity.GetTagItem(key) switch
        {
            long number => number,
            int number => number,
            _ => -1
        };
        if (value >= 0)
        {
            destination = Math.Max(destination, value);
        }
    }

    private static long Increment(long value) => value == long.MaxValue ? value : value + 1;

    private string Snapshot()
        => string.Format(CultureInfo.InvariantCulture, SnapshotTemplate, observed, ensureCount, apiCount,
            errorCount, timeoutCount, configKnown, configExists, lockMs, fileMs, readMs, buildMs, retries, phases);
}
