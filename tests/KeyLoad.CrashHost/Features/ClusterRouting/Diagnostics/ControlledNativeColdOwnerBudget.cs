using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal readonly struct ControlledNativeColdOwnerBudget
{
    private readonly long? ownerStarted;

    internal ControlledNativeColdOwnerBudget(TimeSpan timeout, TimeProvider clock, List<Exception> captureFailures)
    {
        Timeout = timeout;
        Clock = clock;
        long? started = null;
        ServerFailureObserver.Observe(() => started = clock.GetTimestamp(), captureFailures);
        ownerStarted = started;
    }

    internal TimeSpan Timeout { get; }
    internal TimeProvider Clock { get; }

    internal Func<Task> CaptureReport(int node, ControlledNativeColdShutdownEvidence.Stage stage,
        Task original, long? observationStarted)
    {
        var captured = Clock.GetTimestamp();
        long? ownerElapsed = ownerStarted is { } ownerStart
            ? Clock.GetElapsedTime(ownerStart, captured).Ticks : null;
        long? observationElapsed = observationStarted is { } observationStart
            ? Clock.GetElapsedTime(observationStart, captured).Ticks : null;
        var message = FormattableString.Invariant($"KEYLOAD_NATIVE_COLD_BUDGET node={node} stage={(int)stage} task={original.Id} timeoutTicks={Timeout.Ticks} clockFrequency={Clock.TimestampFrequency} systemClock={ReferenceEquals(Clock, TimeProvider.System)} ownerStartCaptured={ownerStarted.HasValue} observationStartCaptured={observationStarted.HasValue} ownerElapsedTicks={ownerElapsed} observationElapsedTicks={observationElapsed}");
        return () => Console.Error.WriteLineAsync(message);
    }
}
