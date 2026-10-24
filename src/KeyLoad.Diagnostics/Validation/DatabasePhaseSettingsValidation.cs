namespace KeyLoad.Diagnostics.ResourceExecution;

/// <summary>Checks the qualified reservation and retry ceilings at the BCL boundary.</summary>
internal static class DatabasePhaseSettingsValidation
{
    private const int TwoStripes = 2;
    private const int MaximumStripeCount = 4;
    private const int MaximumCasAttempts = 4;

    internal static void Validate(int stripeCount, int maximumCasAttempts)
    {
        if (stripeCount is not (1 or TwoStripes or MaximumStripeCount))
        {
            throw new ArgumentOutOfRangeException(nameof(stripeCount));
        }

        ValidateMaximumCasAttempts(maximumCasAttempts);
    }

    internal static void ValidateMaximumCasAttempts(int maximumCasAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCasAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCasAttempts, MaximumCasAttempts);
    }
}
