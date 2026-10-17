namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Checks the qualified reservation and retry ceilings at the BCL boundary.</summary>
internal static class DatabasePhaseSettingsValidation
{
    private const int SingleStripe = 1;
    private const int TwoStripes = 2;
    private const int MaximumStripeCount = 4;
    private const int MinimumCasAttempts = 1;
    private const int MaximumCasAttempts = 4;

    internal static void Validate(int stripeCount, int maximumCasAttempts)
    {
        if (stripeCount is not (SingleStripe or TwoStripes or MaximumStripeCount))
        {
            throw new ArgumentOutOfRangeException(nameof(stripeCount));
        }

        ValidateMaximumCasAttempts(maximumCasAttempts);
    }

    internal static void ValidateMaximumCasAttempts(int maximumCasAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCasAttempts, MinimumCasAttempts);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCasAttempts, MaximumCasAttempts);
    }
}
