using System.Globalization;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Contains only closed, privacy-safe lifecycle fields selected for startup evidence.</summary>
internal readonly record struct ComparisonLifecycleRecord(
    DateTimeOffset ObservedAt,
    string Name,
    long ObserverSequence,
    string State,
    string Health,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? StoppedAt,
    int? ExitCode)
{
    private const int MaximumOutputLines = 80;
    private const int MaximumOutputBytes = 8 * 1024;
    private const int ObserverLineReserve = 1;
    private const string OtherState = "Other";
    private const string OtherHealth = "Other";

    public static ComparisonLifecycleRecord FromNative(string name, CustomResourceSnapshot snapshot, long observerSequence)
        => new(TimeProvider.System.GetUtcNow(), name, observerSequence,
            StateCategory(snapshot.State?.Text, snapshot.IsHidden), HealthCategory(snapshot.HealthStatus),
            snapshot.CreationTimeStamp, snapshot.StartTimeStamp, snapshot.StopTimeStamp, snapshot.ExitCode);

    public static ComparisonLifecycleRecord NotObserved(string name, long observerSequence)
        => new(TimeProvider.System.GetUtcNow(), name, observerSequence, "NotObserved", "None", null, null, null, null);

    public string FormatLine() => string.Create(CultureInfo.InvariantCulture,
        $"event at={ObservedAt:O} name={Name} observerSequence={ObserverSequence} state={State} health={Health} created={CreatedAt:O} started={StartedAt:O} stopped={StoppedAt:O} exit={ExitCode}");

    public static string FormatFailureOutput(ComparisonLifecycleRecord[] current,
        ComparisonLifecycleRecord[] history, string observerCategory)
    {
        var output = new StringBuilder(MaximumOutputBytes);
        var bytes = 0;
        var lines = 0;
        foreach (var record in current)
        {
            AppendLine(output, record.FormatLine(), ref bytes, ref lines);
        }

        AppendNewestTail(output, history, ref bytes, ref lines);
        AppendLine(output, $"observer={observerCategory}", ref bytes, ref lines);
        return output.ToString();
    }

    private static string StateCategory(string? state, bool isHidden)
    {
        if (isHidden)
        {
            return "Hidden";
        }

        if (string.Equals(state, KnownResourceStates.Starting, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.Starting);
        }
        if (string.Equals(state, KnownResourceStates.Running, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.Running);
        }
        if (string.Equals(state, KnownResourceStates.FailedToStart, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.FailedToStart);
        }
        if (string.Equals(state, KnownResourceStates.RuntimeUnhealthy, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.RuntimeUnhealthy);
        }
        if (string.Equals(state, KnownResourceStates.Stopping, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.Stopping);
        }
        if (string.Equals(state, KnownResourceStates.Exited, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.Exited);
        }
        if (string.Equals(state, KnownResourceStates.Finished, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.Finished);
        }
        if (string.Equals(state, KnownResourceStates.Waiting, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.Waiting);
        }
        if (string.Equals(state, KnownResourceStates.NotStarted, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.NotStarted);
        }
        if (string.Equals(state, KnownResourceStates.Building, StringComparison.Ordinal))
        {
            return nameof(KnownResourceStates.Building);
        }
        return state is null ? "None" : OtherState;
    }

    private static string HealthCategory(HealthStatus? health) => health switch
    {
        HealthStatus.Healthy => nameof(HealthStatus.Healthy),
        HealthStatus.Degraded => nameof(HealthStatus.Degraded),
        HealthStatus.Unhealthy => nameof(HealthStatus.Unhealthy),
        null => "None",
        _ => OtherHealth
    };

    private static void AppendNewestTail(StringBuilder output, ComparisonLifecycleRecord[] history,
        ref int bytes, ref int lines)
    {
        var lineLimit = MaximumOutputLines - lines - ObserverLineReserve;
        var byteLimit = MaximumOutputBytes - bytes - Utf8Bytes("observer=DiagnosticFaulted");
        var newest = new List<string>(Math.Min(history.Length, lineLimit));
        var newestBytes = 0;
        for (var index = history.Length - 1; index >= 0 && newest.Count < lineLimit; index--)
        {
            var line = history[index].FormatLine();
            var lineBytes = Utf8Bytes(line);
            if (newestBytes + lineBytes > byteLimit)
            {
                break;
            }

            newest.Add(line);
            newestBytes += lineBytes;
        }

        for (var index = newest.Count - 1; index >= 0; index--)
        {
            AppendLine(output, newest[index], ref bytes, ref lines);
        }
    }

    private static void AppendLine(StringBuilder output, string line, ref int bytes, ref int lines)
    {
        var lineBytes = Utf8Bytes(line);
        if (lines >= MaximumOutputLines || bytes + lineBytes > MaximumOutputBytes)
        {
            return;
        }

        output.AppendLine(line);
        bytes += lineBytes;
        lines++;
    }

    private static int Utf8Bytes(string value) => Encoding.UTF8.GetByteCount(value) + Encoding.UTF8.GetByteCount(Environment.NewLine);
}
