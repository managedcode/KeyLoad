using System.Diagnostics;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using System.Globalization;
using System.Text;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ScaleServerResourceCancellationNativeFixture
{
    internal const string CancellationMissingMessage = "The owned scale probe did not preserve caller cancellation.";
    internal const string StartMarkerName = "child-started.txt";
    private const string StartSource = "import { writeFileSync } from 'node:fs'; writeFileSync(process.argv[1], String(process.pid) + '\\nready'); setInterval(() => {}, Number(process.argv[2]));";
    private const string HealthySource = "process.stdout.write(process.argv[1]);";
    private const string InputTypeArgument = "--input-type=module";
    private const string EvaluateArgument = "-e";
    private const string MarkerTypeMessage = "The owned child readiness marker is not a regular file.";
    private const string MarkerBoundMessage = "The owned child readiness marker exceeded its validated bound.";
    private const string MarkerReadySuffix = "\nready";
    private const int InitialTotal = 0;
    private const int ExtraByteCount = 1;
    private const int ProcessIdStart = 0;
    private const FileAttributes NoAttributes = (FileAttributes)0;

    internal static string[] StartArguments(string markerPath, TimeSpan cadence)
        => [InputTypeArgument, EvaluateArgument, StartSource, markerPath,
            cadence.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)];

    internal static string[] HealthyArguments(string output)
        => [InputTypeArgument, EvaluateArgument, HealthySource, output];

    internal static async Task<ScaleServerResourceChildIdentity> WaitForStartedChildAsync(string markerPath,
        ScaleServerResourceSampleBudget budget, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(budget.Settings.CleanupThreshold, budget.TimeProvider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var child = await TryObserveStartedChildAsync(markerPath, budget.Settings.MaxFileBytes, deadline.Token);
            if (child is not null)
            {
                return child.Value;
            }

            await Task.Delay(budget.Settings.Cadence, budget.TimeProvider, deadline.Token);
        }
    }

    internal static bool IsOriginalChildRunning(ScaleServerResourceChildIdentity child)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(child.ProcessId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        using (process)
        {
            process.Refresh();
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == child.StartTimeUtcTicks;
        }
    }

    private static async Task<ScaleServerResourceChildIdentity?> TryObserveStartedChildAsync(string markerPath,
        int maximumMarkerBytes, CancellationToken cancellationToken)
    {
        int? processId;
        try
        {
            processId = await ReadProcessIdAsync(markerPath, maximumMarkerBytes, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        if (processId is null)
        {
            return null;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId.Value);
        }
        catch (ArgumentException)
        {
            return null;
        }

        using (process)
        {
            process.Refresh();
            return process.HasExited
                ? null
                : new ScaleServerResourceChildIdentity(processId.Value, process.StartTime.ToUniversalTime().Ticks);
        }
    }

    private static async Task<int?> ReadProcessIdAsync(string markerPath, int maximumMarkerBytes,
        CancellationToken cancellationToken)
    {
        var attributes = File.GetAttributes(markerPath);
        ValidateMarkerAttributes(attributes);
        await using var marker = OpenMarker(markerPath, maximumMarkerBytes);
        if (marker.Length > maximumMarkerBytes)
        {
            throw new InvalidDataException(MarkerBoundMessage);
        }

        var bytes = await ReadBoundedMarkerAsync(marker, maximumMarkerBytes, cancellationToken);
        return ParseProcessId(bytes);
    }

    private static void ValidateMarkerAttributes(FileAttributes attributes)
    {
        const FileAttributes NonRegularMarker = FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device;
        if ((attributes & NonRegularMarker) != NoAttributes)
        {
            throw new InvalidDataException(MarkerTypeMessage);
        }
    }

    private static FileStream OpenMarker(string markerPath, int maximumMarkerBytes)
        => new(markerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, maximumMarkerBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<byte[]> ReadBoundedMarkerAsync(FileStream marker, int maximumMarkerBytes,
        CancellationToken cancellationToken)
    {
        var bytes = new byte[maximumMarkerBytes];
        var total = InitialTotal;
        while (total < bytes.Length)
        {
            var count = await marker.ReadAsync(bytes.AsMemory(total), cancellationToken);
            if (count == InitialTotal)
            {
                break;
            }

            total += count;
        }

        await ThrowIfMarkerExceededBoundAsync(marker, total, maximumMarkerBytes, cancellationToken);
        return bytes.AsSpan(ProcessIdStart, total).ToArray();
    }

    private static async Task ThrowIfMarkerExceededBoundAsync(FileStream marker, int total, int maximumMarkerBytes,
        CancellationToken cancellationToken)
    {
        if (total != maximumMarkerBytes)
        {
            return;
        }

        var extraByte = new byte[ExtraByteCount];
        if (await marker.ReadAsync(extraByte.AsMemory(), cancellationToken) != InitialTotal || marker.Length > maximumMarkerBytes)
        {
            throw new InvalidDataException(MarkerBoundMessage);
        }
    }

    private static int? ParseProcessId(byte[] bytes)
    {
        var marker = Encoding.ASCII.GetString(bytes);
        if (!marker.EndsWith(MarkerReadySuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var processIdLength = marker.Length - MarkerReadySuffix.Length;
        return int.TryParse(marker.AsSpan(ProcessIdStart, processIdLength), NumberStyles.None, CultureInfo.InvariantCulture,
            out var processId) ? processId : null;
    }


}

internal readonly record struct ScaleServerResourceChildIdentity(int ProcessId, long StartTimeUtcTicks);
