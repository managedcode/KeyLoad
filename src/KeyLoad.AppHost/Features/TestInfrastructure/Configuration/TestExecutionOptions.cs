using KeyLoad;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Central startup policy for Aspire-owned test execution and child process cleanup.</summary>
[ConfigurationOptions]
internal sealed class TestExecutionOptions
{
    internal const string SectionName = "KeyLoadTests:Execution";
    internal const string ValidationMessage = "Aspire test execution durations and output limits exceed supported bounds.";
    private const int OrdinaryMinutes = 30;
    private const int ClusterMinutes = 60;
    private const int IntensiveMinutes = 140;
    private const int MaximumMinutes = 180;
    private const int ApplicationCleanupSeconds = 30;
    private const int ImageCleanupSeconds = 45;
    private const int GraceSeconds = 1;
    private const int SettlementSeconds = 5;
    private const int PollMilliseconds = 100;
    private const int DefaultOutputCharacters = 4_096;
    private const int MinimumOutputCharacters = 1;
    private const int MaximumOutputCharacters = 65_536;

    public TimeSpan OrdinaryTimeout { get; set; } = TimeSpan.FromMinutes(OrdinaryMinutes);
    public TimeSpan ClusterTimeout { get; set; } = TimeSpan.FromMinutes(ClusterMinutes);
    public TimeSpan IntensiveTimeout { get; set; } = TimeSpan.FromMinutes(IntensiveMinutes);
    public TimeSpan ApplicationCleanupTimeout { get; set; } = TimeSpan.FromSeconds(ApplicationCleanupSeconds);
    public TimeSpan ImageCleanupTimeout { get; set; } = TimeSpan.FromSeconds(ImageCleanupSeconds);
    public TimeSpan TerminationGrace { get; set; } = TimeSpan.FromSeconds(GraceSeconds);
    public TimeSpan ProcessSettlementTimeout { get; set; } = TimeSpan.FromSeconds(SettlementSeconds);
    public TimeSpan ProcessExitPollInterval { get; set; } = TimeSpan.FromMilliseconds(PollMilliseconds);
    public int CleanupOutputCharacters { get; set; } = DefaultOutputCharacters;

    internal bool IsValid() => Bounded(OrdinaryTimeout) && Bounded(ClusterTimeout) && Bounded(IntensiveTimeout)
        && Bounded(ApplicationCleanupTimeout) && Bounded(ImageCleanupTimeout) && Bounded(TerminationGrace)
        && Bounded(ProcessSettlementTimeout) && Bounded(ProcessExitPollInterval)
        && CleanupOutputCharacters is >= MinimumOutputCharacters and <= MaximumOutputCharacters;

    internal static bool Bounded(TimeSpan value) => value > TimeSpan.Zero && value.Ticks <= MaximumMinutes * TimeSpan.TicksPerMinute;
}
