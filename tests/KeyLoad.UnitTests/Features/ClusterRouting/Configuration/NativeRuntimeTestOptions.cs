namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Central timing profile shared by real native journal and job fixtures.</summary>
[ConfigurationOptions]
internal sealed record NativeRuntimeTestOptions
{
    private const int StartupSeconds = 30;
    private const int ShutdownSeconds = 30;
    private const int CompletionSeconds = 20;
    private const int PollMilliseconds = 25;
    private const int RetryMilliseconds = 50;
    private const int HeldJobSeconds = 1;
    private const int MaximumTimeoutSeconds = 60;
    private const int MinimumDurationMilliseconds = 1;

    internal TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(StartupSeconds);
    internal TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(ShutdownSeconds);
    internal TimeSpan CompletionTimeout { get; init; } = TimeSpan.FromSeconds(CompletionSeconds);
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(PollMilliseconds);
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(RetryMilliseconds);
    internal TimeSpan HeldJobDelay { get; init; } = TimeSpan.FromSeconds(HeldJobSeconds);

    internal bool IsValid() => Bounded(StartupTimeout) && Bounded(ShutdownTimeout)
        && Bounded(CompletionTimeout) && Bounded(PollInterval) && Bounded(RetryDelay) && Bounded(HeldJobDelay);

    private static bool Bounded(TimeSpan duration) => duration >= TimeSpan.FromMilliseconds(MinimumDurationMilliseconds)
        && duration <= TimeSpan.FromSeconds(MaximumTimeoutSeconds);
}
