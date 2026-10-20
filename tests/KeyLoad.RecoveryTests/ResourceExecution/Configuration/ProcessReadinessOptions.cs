namespace KeyLoad.RecoveryTests;

/// <summary>Validated execution child ownership readiness bounded by the original test policy.</summary>
[ConfigurationOptions]
internal sealed class ProcessReadinessOptions
{
    private const int MaximumOwnershipSeconds = 5;
    private const int MaximumPollMilliseconds = 25;
    private const string Invalid = "Execution process ownership readiness exceeds the original bounded policy.";

    internal TimeSpan OwnershipTimeout { get; init; } = TimeSpan.FromSeconds(MaximumOwnershipSeconds);
    internal TimeSpan OwnershipPollInterval { get; init; } = TimeSpan.FromMilliseconds(MaximumPollMilliseconds);

    internal void Validate()
    {
        if (OwnershipTimeout <= TimeSpan.Zero || OwnershipTimeout.TotalSeconds > MaximumOwnershipSeconds
            || OwnershipPollInterval <= TimeSpan.Zero
            || OwnershipPollInterval.TotalMilliseconds > MaximumPollMilliseconds
            || OwnershipPollInterval >= OwnershipTimeout)
        {
            throw new InvalidOperationException(Invalid);
        }
    }
}
