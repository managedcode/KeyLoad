namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeReadCutValidation
{
    internal const int MaximumRecords = 5_000_000;
    internal const long MaximumExaminedBytes = 1_073_741_824;
    internal static readonly TimeSpan MaximumElapsed = TimeSpan.FromMinutes(1);
    private const string InvalidLimitsMessage = "The native read-cut limits are invalid.";

    internal static void Validate(ZoneTreeReadCutLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxRecords is < 1 or > MaximumRecords
            || limits.MaxExaminedBytes is < 1 or > MaximumExaminedBytes
            || limits.MaxElapsed <= TimeSpan.Zero || limits.MaxElapsed > MaximumElapsed)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLimitsMessage);
        }
    }
}
