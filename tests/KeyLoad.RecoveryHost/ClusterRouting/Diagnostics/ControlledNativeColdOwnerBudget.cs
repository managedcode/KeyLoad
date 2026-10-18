using KeyLoad.Server;

namespace KeyLoad.RecoveryHost.Features.ClusterRouting;

internal readonly struct ControlledNativeColdOwnerBudget
{
    private const string Prefix = "KEYLOAD_NATIVE_COLD_BUDGET";
    private const string NodeField = " node=";
    private const string StageField = " stage=";
    private const string TaskField = " task=";
    private const string TimeoutField = " timeoutTicks=";
    private const string FrequencyField = " clockFrequency=";
    private const string SystemClockField = " systemClock=";
    private const string OwnerCapturedField = " ownerStartCaptured=";
    private const string ObservationCapturedField = " observationStartCaptured=";
    private const string OwnerElapsedField = " ownerElapsedTicks=";
    private const string ObservationElapsedField = " observationElapsedTicks=";
    private readonly long? ownerStarted;

    internal ControlledNativeColdOwnerBudget(TimeSpan timeout, TimeProvider clock, List<Exception> captureFailures)
    {
        Timeout = timeout;
        Clock = clock;
        long? started = null;
        ServerFailureObserver.Observe(() => started = clock.GetTimestamp(), captureFailures);
        ownerStarted = started;
    }

    internal TimeSpan Timeout
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }

    internal Func<Task> CaptureReport(int node, ControlledNativeColdShutdownEvidence.Stage stage,
        Task original, long? observationStarted)
    {
        var captured = Clock.GetTimestamp();
        long? ownerElapsed = ownerStarted is { } ownerStart
            ? Clock.GetElapsedTime(ownerStart, captured).Ticks : null;
        long? observationElapsed = observationStarted is { } observationStart
            ? Clock.GetElapsedTime(observationStart, captured).Ticks : null;
        var message = FormattableString.Invariant($"{Prefix}{NodeField}{node}{StageField}{(int)stage}{TaskField}{original.Id}{TimeoutField}{Timeout.Ticks}{FrequencyField}{Clock.TimestampFrequency}{SystemClockField}{ReferenceEquals(Clock, TimeProvider.System)}{OwnerCapturedField}{ownerStarted.HasValue}{ObservationCapturedField}{observationStarted.HasValue}{OwnerElapsedField}{ownerElapsed}{ObservationElapsedField}{observationElapsed}");
        return () => Console.Error.WriteLineAsync(message);
    }
}
