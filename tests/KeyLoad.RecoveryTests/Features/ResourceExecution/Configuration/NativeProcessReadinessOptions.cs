namespace KeyLoad.RecoveryTests;

/// <summary>Validated native child ownership readiness bounded by the original test policy.</summary>
[ConfigurationOptions]
internal sealed class NativeProcessReadinessOptions
{
    private const int MaximumOwnershipSeconds = 5;
    private const int MaximumPollMilliseconds = 25;
    private const string Invalid = "Native process ownership readiness exceeds the original bounded policy.";

    internal TimeSpan OwnershipTimeout { get; init; } = TimeSpan.FromSeconds(MaximumOwnershipSeconds);
    internal TimeSpan OwnershipPollInterval { get; init; } = TimeSpan.FromMilliseconds(MaximumPollMilliseconds);

    internal void Validate()
    {
        if (OwnershipTimeout <= TimeSpan.Zero || OwnershipTimeout > TimeSpan.FromSeconds(MaximumOwnershipSeconds)
            || OwnershipPollInterval <= TimeSpan.Zero
            || OwnershipPollInterval > TimeSpan.FromMilliseconds(MaximumPollMilliseconds)
            || OwnershipPollInterval >= OwnershipTimeout)
        { throw new InvalidOperationException(Invalid); }
    }
}
