namespace KeyLoad.ProcessRecoveryTests;

[ConfigurationOptions]
internal sealed class RecoveryTrialOptions
{
    internal TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(90);
    internal TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    internal TimeSpan ProjectionTimeout { get; init; } = TimeSpan.FromSeconds(20);
    internal TimeSpan CompositionTimeout { get; init; } = TimeSpan.FromSeconds(25);
    internal TimeSpan ShortRecoveryTimeout { get; init; } = TimeSpan.FromSeconds(15);
    internal TimeSpan SearchProjectionTimeout { get; init; } = TimeSpan.FromSeconds(45);
    internal TimeSpan ReadinessObservationSlack { get; init; } = TimeSpan.FromSeconds(1);
    internal TimeSpan RetentionVerificationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    internal int FileBufferBytes { get; init; } = 4096;
    internal int ProgressReadBufferBytes { get; init; } = 1024;
    internal int MaximumConcurrentStorageTrials { get; init; } = 4;

    internal void Validate()
    {
        TimeSpan[] bounds = [CommandTimeout, CleanupTimeout, ProjectionTimeout,
            CompositionTimeout, ShortRecoveryTimeout, SearchProjectionTimeout,
            ReadinessObservationSlack, RetentionVerificationTimeout];
        foreach (var bound in bounds)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bound, TimeSpan.Zero);
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumConcurrentStorageTrials);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FileBufferBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ProgressReadBufferBytes);
    }
}
