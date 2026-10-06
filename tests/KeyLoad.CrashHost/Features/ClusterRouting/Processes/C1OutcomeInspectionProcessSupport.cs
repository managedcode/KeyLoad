using System.Diagnostics;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal static class C1OutcomeInspectionDeadline
{
    private const string CancellationMessage = "The outcome inspection process was canceled.";
    private const string ExecutionTimeoutMessage = "The outcome inspection process exceeded its execution deadline.";
    private const string CleanupTimeoutMessage = "The outcome inspection process exceeded its cleanup deadline.";

    internal static async Task WaitAsync(Process process, Task all, List<Exception> failures,
        IOptions<CrashHostExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        var settings = executionOptions.Value;
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopped = Task.Delay(Timeout.InfiniteTimeSpan, timer.Token);
        var deadline = Task.Delay(settings.InspectionExecutionTimeout, timer.Token);
        var first = await Task.WhenAny(all, stopped, deadline).ConfigureAwait(false);
        if (first == all)
        {
            await timer.CancelAsync().ConfigureAwait(false);
            await ObserveTimerAsync(stopped).ConfigureAwait(false);
            await ObserveTimerAsync(deadline).ConfigureAwait(false);
            return;
        }
        failures.Add(cancellationToken.IsCancellationRequested
            ? new OperationCanceledException(CancellationMessage, cancellationToken)
            : new TimeoutException(ExecutionTimeoutMessage));
        ServerFailureObserver.Observe(() => C1OutcomeInspectionProcessIo.Kill(process), failures);
        using var cleanupTimer = new CancellationTokenSource();
        var cleanup = Task.Delay(settings.InspectionCleanupTimeout, cleanupTimer.Token);
        if (await Task.WhenAny(all, cleanup).ConfigureAwait(false) != all)
        {
            failures.Add(new TimeoutException(CleanupTimeoutMessage));
            ServerFailureObserver.Observe(() => C1OutcomeInspectionProcessIo.Kill(process), failures);
        }
        await cleanupTimer.CancelAsync().ConfigureAwait(false);
        await ObserveTimerAsync(cleanup).ConfigureAwait(false);
        await timer.CancelAsync().ConfigureAwait(false);
        await ObserveTimerAsync(stopped).ConfigureAwait(false);
        await ObserveTimerAsync(deadline).ConfigureAwait(false);
    }

    private static async Task ObserveTimerAsync(Task timer)
    {
        try
        { await timer.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}

internal static class C1OutcomeInspectionProcessIo
{
    internal static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
        }
        catch (InvalidOperationException) when (process.HasExited)
        { }
    }

    internal static async Task WriteInputAsync(Process child, ReadOnlyMemory<byte> payload)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(
            () => child.StandardInput.BaseStream.WriteAsync(payload).AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(
            child.StandardInput.BaseStream.FlushAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(child.StandardInput.Close, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

internal sealed class C1OutcomeInspectionCapture
{
    private const int GetBytesStartEmptyCount = 0;

    private readonly CrashHostExecutionOptions settings;
    private readonly byte[] retained;
    private readonly byte[] buffer;

    internal C1OutcomeInspectionCapture(IOptions<CrashHostExecutionOptions> executionOptions)
    {
        settings = executionOptions.Value;
        retained = new byte[settings.InspectionMaximumOutputBytes];
        buffer = new byte[settings.InspectionReadBufferBytes];
    }
    private long observedBytes;

    internal bool Exceeded => observedBytes > settings.InspectionMaximumOutputBytes;
    internal byte[] Bytes => retained.AsSpan(GetBytesStartEmptyCount, checked((int)Math.Min(observedBytes, settings.InspectionMaximumOutputBytes))).ToArray();

    internal async Task DrainAsync(Stream stream)
    {
        const int EmptyRead = 0;
        const int StartEmptyCount = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read == EmptyRead)
            { return; }
            var offset = checked((int)Math.Min(observedBytes, settings.InspectionMaximumOutputBytes));
            var copy = Math.Min(settings.InspectionMaximumOutputBytes - offset, read);
            buffer.AsSpan(StartEmptyCount, copy).CopyTo(retained.AsSpan(offset));
            observedBytes = checked(observedBytes + read);
        }
    }
}
