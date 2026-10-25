namespace KeyLoad.ProcessRecoveryTests.OrleansExecution;

/// <summary>Central timing profile shared by real execution journal and job fixtures.</summary>
[ConfigurationOptions]
internal sealed record RuntimeTestOptions
{
    // A host start builds the MCP tool-search graph; full-text indexing in that graph is serialized process-wide,
    // so parallel host starts queue behind each other.
    private const int StartupSeconds = 60;
    private const int ShutdownSeconds = 30;
    private const int CompletionSeconds = 20;
    private const int PollMilliseconds = 25;
    private const int RetryMilliseconds = 50;
    private const int RestartHeldJobSeconds = 110;
    private const int MaximumTimeoutSeconds = 60;
    private const int MaximumRestartTimeoutSeconds = 130;

    internal TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(StartupSeconds);
    internal TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(ShutdownSeconds);
    internal TimeSpan CompletionTimeout { get; init; } = TimeSpan.FromSeconds(CompletionSeconds);
    internal TimeSpan OrdinaryCollectionAge { get; init; } = TimeSpan.FromDays(1);
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(PollMilliseconds);
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(RetryMilliseconds);
    internal TimeSpan HeldJobDelay { get; init; } = TimeSpan.FromSeconds(1);
    internal TimeSpan RestartHeldJobDelay { get; init; } = TimeSpan.FromSeconds(RestartHeldJobSeconds);
    internal TimeSpan RestartCompletionTimeout { get; init; } = TimeSpan.FromSeconds(MaximumRestartTimeoutSeconds);

    internal bool IsValid() => Bounded(StartupTimeout) && Bounded(ShutdownTimeout)
        && Bounded(CompletionTimeout) && Bounded(PollInterval) && Bounded(RetryDelay)
        && Bounded(HeldJobDelay) && RestartBounded(RestartHeldJobDelay)
        && RestartBounded(RestartCompletionTimeout)
        && RestartHeldJobDelay >= StartupTimeout + ShutdownTimeout + CompletionTimeout
        && RestartHeldJobDelay + CompletionTimeout <= RestartCompletionTimeout
        && OrdinaryCollectionAge >= StartupTimeout + ShutdownTimeout + CompletionTimeout
        && OrdinaryCollectionAge.TotalDays <= 1;

    private static bool Bounded(TimeSpan duration) => duration.TotalMilliseconds >= 1
        && duration.TotalSeconds <= MaximumTimeoutSeconds;

    private static bool RestartBounded(TimeSpan duration)
        => duration.TotalMilliseconds >= 1
            && duration.TotalSeconds <= MaximumRestartTimeoutSeconds;
}
