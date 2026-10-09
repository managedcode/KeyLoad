
namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Central startup policy for Aspire-owned test execution and child process cleanup.</summary>
[ConfigurationOptions]
internal sealed class TestExecutionOptions
{
    internal const string SectionName = "KeyLoadTests:Execution";
    internal const string ValidationMessage = "Aspire test execution durations, output limits and parallelism exceed supported bounds.";
    private const int OrdinaryMinutes = 30;
    private const int ClusterMinutes = 60;
    private const int IntensiveMinutes = 140;
    private const int NativeVectorMinutes = 145;
    private const int MaximumMinutes = 180;
    private const int ApplicationCleanupSeconds = 30;
    private const int ImageCleanupSeconds = 45;
    private const int GraceSeconds = 1;
    private const int SettlementSeconds = 5;
    private const int PollMilliseconds = 100;
    private const int ProtectedMovementSetupSeconds = 50;
    private const int ProtectedMovementOutcomeSeconds = 25;
    private const int ProtectedMovementCleanupSeconds = 30;
    private const int MaximumArgumentCharacters = 4096;
    private const int DefaultOutputCharacters = 4_096;
    private const int MinimumOutputCharacters = 1;
    private const int MaximumOutputCharacters = 65_536;
    private const int DefaultMaximumParallelTests = 20;
    private const int MinimumMaximumParallelTests = 1;
    private const int MaximumMaximumParallelTests = 50;

    public TimeSpan OrdinaryTimeout { get; set; } = TimeSpan.FromMinutes(OrdinaryMinutes);
    public TimeSpan ClusterTimeout { get; set; } = TimeSpan.FromMinutes(ClusterMinutes);
    public TimeSpan IntensiveTimeout { get; set; } = TimeSpan.FromMinutes(IntensiveMinutes);
    public TimeSpan NativeControlTimeout { get; set; } = TimeSpan.FromMinutes(ClusterMinutes);
    public TimeSpan NativeScaledTimeout { get; set; } = TimeSpan.FromMinutes(IntensiveMinutes);
    public TimeSpan NativeVectorTimeout { get; set; } = TimeSpan.FromMinutes(NativeVectorMinutes);
    public TimeSpan ApplicationCleanupTimeout { get; set; } = TimeSpan.FromSeconds(ApplicationCleanupSeconds);
    public TimeSpan ImageCleanupTimeout { get; set; } = TimeSpan.FromSeconds(ImageCleanupSeconds);
    public TimeSpan TerminationGrace { get; set; } = TimeSpan.FromSeconds(GraceSeconds);
    public TimeSpan ProcessSettlementTimeout { get; set; } = TimeSpan.FromSeconds(SettlementSeconds);
    public TimeSpan ProtectedMovementSetupRequestLifetime { get; set; } = TimeSpan.FromSeconds(ProtectedMovementSetupSeconds);
    public TimeSpan ProtectedMovementOutcomeRequestLifetime { get; set; } = TimeSpan.FromSeconds(ProtectedMovementOutcomeSeconds);
    public TimeSpan ProtectedMovementCaptureCleanupTimeout { get; set; } = TimeSpan.FromSeconds(ProtectedMovementCleanupSeconds);
    public TimeSpan DatabaseReadinessPollInterval { get; set; } = TimeSpan.FromMilliseconds(PollMilliseconds);
    public TimeSpan ProcessExitPollInterval { get; set; } = TimeSpan.FromMilliseconds(PollMilliseconds);
    public int CleanupOutputCharacters { get; set; } = DefaultOutputCharacters;

    public int MaximumFilterCharacters { get; set; } = MaximumArgumentCharacters;
    public int MaximumPathCharacters { get; set; } = MaximumArgumentCharacters;
    public int MaximumParallelTests { get; set; } = DefaultMaximumParallelTests;

    internal bool IsValid() => Bounded(OrdinaryTimeout) && Bounded(ClusterTimeout) && Bounded(IntensiveTimeout)
        && Bounded(NativeControlTimeout) && Bounded(NativeScaledTimeout) && Bounded(NativeVectorTimeout)
        && Bounded(ApplicationCleanupTimeout) && Bounded(ImageCleanupTimeout) && Bounded(TerminationGrace)
        && Bounded(ProcessSettlementTimeout) && Bounded(ProcessExitPollInterval) && Bounded(DatabaseReadinessPollInterval)
        && Bounded(ProtectedMovementSetupRequestLifetime)
        && ProtectedMovementSetupRequestLifetime.Ticks <= ProtectedMovementSetupSeconds * TimeSpan.TicksPerSecond
        && Bounded(ProtectedMovementOutcomeRequestLifetime)
        && ProtectedMovementOutcomeRequestLifetime.Ticks <= ProtectedMovementOutcomeSeconds * TimeSpan.TicksPerSecond
        && Bounded(ProtectedMovementCaptureCleanupTimeout)
        && ProtectedMovementCaptureCleanupTimeout.Ticks <= ProtectedMovementCleanupSeconds * TimeSpan.TicksPerSecond
        && MaximumFilterCharacters is >= MinimumOutputCharacters and <= MaximumArgumentCharacters
        && MaximumPathCharacters is >= MinimumOutputCharacters and <= MaximumArgumentCharacters
        && MaximumParallelTests is >= MinimumMaximumParallelTests and <= MaximumMaximumParallelTests
        && CleanupOutputCharacters is >= MinimumOutputCharacters and <= MaximumOutputCharacters;

    internal static bool Bounded(TimeSpan value) => value > TimeSpan.Zero && value.Ticks <= MaximumMinutes * TimeSpan.TicksPerMinute;
}
