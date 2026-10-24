
namespace KeyLoad.AppHost.TestInfrastructure;

/// <summary>Central startup policy for Aspire-owned test execution and child process cleanup.</summary>
[ConfigurationOptions]
internal sealed class TestExecutionOptions
{
    internal const string SectionName = "KeyLoadTests:Execution";
    internal const string ValidationMessage = "Aspire test execution durations, output limits and parallelism exceed supported bounds.";
    private const int OrdinaryMinutes = 30;
    private const int ClusterMinutes = 60;
    private const int IntensiveMinutes = 140;
    private const int VectorMinutes = 145;
    private const int MaximumMinutes = 180;
    private const int ApplicationCleanupSeconds = 30;
    private const int ImageCleanupSeconds = 45;
    private const int SettlementSeconds = 5;
    private const int PollMilliseconds = 100;
    private const int ProtectedMovementSetupSeconds = 50;
    private const int ProtectedMovementOutcomeSeconds = 25;
    private const int ProtectedMovementCleanupSeconds = 30;
    private const int MaximumArgumentCharacters = 4096;
    private const int DefaultOutputCharacters = 4_096;
    private const int MaximumOutputCharacters = 65_536;
    private const int DefaultMaximumParallelTests = 50;
    private const int MaximumMaximumParallelTests = 50;
    private const int ObserverWindowSeconds = 1;
    private const int MaximumObserverWindowSeconds = 10;

    public TimeSpan OrdinaryTimeout { get; set; } = TimeSpan.FromMinutes(OrdinaryMinutes);
    public TimeSpan ClusterTimeout { get; set; } = TimeSpan.FromMinutes(ClusterMinutes);
    public TimeSpan IntensiveTimeout { get; set; } = TimeSpan.FromMinutes(IntensiveMinutes);
    public TimeSpan ControlTimeout { get; set; } = TimeSpan.FromMinutes(ClusterMinutes);
    public TimeSpan ScaledTimeout { get; set; } = TimeSpan.FromMinutes(IntensiveMinutes);
    public TimeSpan VectorTimeout { get; set; } = TimeSpan.FromMinutes(VectorMinutes);
    public TimeSpan ApplicationCleanupTimeout { get; set; } = TimeSpan.FromSeconds(ApplicationCleanupSeconds);
    public TimeSpan ImageCleanupTimeout { get; set; } = TimeSpan.FromSeconds(ImageCleanupSeconds);
    public TimeSpan TerminationGrace { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ProcessSettlementTimeout { get; set; } = TimeSpan.FromSeconds(SettlementSeconds);
    public TimeSpan ProtectedMovementSetupRequestLifetime { get; set; } = TimeSpan.FromSeconds(ProtectedMovementSetupSeconds);
    public TimeSpan ProtectedMovementOutcomeRequestLifetime { get; set; } = TimeSpan.FromSeconds(ProtectedMovementOutcomeSeconds);
    public TimeSpan ProtectedMovementCaptureCleanupTimeout { get; set; } = TimeSpan.FromSeconds(ProtectedMovementCleanupSeconds);
    public TimeSpan DatabaseReadinessPollInterval { get; set; } = TimeSpan.FromMilliseconds(PollMilliseconds);
    public TimeSpan ProcessExitPollInterval { get; set; } = TimeSpan.FromMilliseconds(PollMilliseconds);
    // Linux derives Process.StartTime from a boot-time estimate cached once per observing process
    // (CLOCK_REALTIME_COARSE minus CLOCK_BOOTTIME), so a second process reading the same PID can differ by the
    // coarse clock granularity. Process identity therefore pairs the PID, executable and compiled-file hashes
    // with a start time inside this window; a reused PID cannot start inside it while the original was observed alive.
    public TimeSpan ProcessStartTimeObserverWindow { get; set; } = TimeSpan.FromSeconds(ObserverWindowSeconds);
    public int CleanupOutputCharacters { get; set; } = DefaultOutputCharacters;

    public int MaximumFilterCharacters { get; set; } = MaximumArgumentCharacters;
    public int MaximumPathCharacters { get; set; } = MaximumArgumentCharacters;
    public int MaximumParallelTests { get; set; } = DefaultMaximumParallelTests;

    internal bool IsValid() => Bounded(OrdinaryTimeout) && Bounded(ClusterTimeout) && Bounded(IntensiveTimeout)
        && Bounded(ControlTimeout) && Bounded(ScaledTimeout) && Bounded(VectorTimeout)
        && Bounded(ApplicationCleanupTimeout) && Bounded(ImageCleanupTimeout) && Bounded(TerminationGrace)
        && Bounded(ProcessSettlementTimeout) && Bounded(ProcessExitPollInterval) && Bounded(DatabaseReadinessPollInterval)
        && Bounded(ProcessStartTimeObserverWindow)
        && ProcessStartTimeObserverWindow.Ticks <= MaximumObserverWindowSeconds * TimeSpan.TicksPerSecond
        && Bounded(ProtectedMovementSetupRequestLifetime)
        && ProtectedMovementSetupRequestLifetime.Ticks <= ProtectedMovementSetupSeconds * TimeSpan.TicksPerSecond
        && Bounded(ProtectedMovementOutcomeRequestLifetime)
        && ProtectedMovementOutcomeRequestLifetime.Ticks <= ProtectedMovementOutcomeSeconds * TimeSpan.TicksPerSecond
        && Bounded(ProtectedMovementCaptureCleanupTimeout)
        && ProtectedMovementCaptureCleanupTimeout.Ticks <= ProtectedMovementCleanupSeconds * TimeSpan.TicksPerSecond
        && MaximumFilterCharacters is >= 1 and <= MaximumArgumentCharacters
        && MaximumPathCharacters is >= 1 and <= MaximumArgumentCharacters
        && MaximumParallelTests is >= 1 and <= MaximumMaximumParallelTests
        && CleanupOutputCharacters is >= 1 and <= MaximumOutputCharacters;

    internal static bool Bounded(TimeSpan value) => value > TimeSpan.Zero && value.Ticks <= MaximumMinutes * TimeSpan.TicksPerMinute;
}
