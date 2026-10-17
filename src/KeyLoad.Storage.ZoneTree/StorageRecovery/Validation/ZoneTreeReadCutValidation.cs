namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeReadCutValidation
{
    private const int MinimumPositiveBudget = 1;
    private const string InvalidLimitsMessage = "The native read-cut limits are invalid.";

    internal static void Validate(ZoneTreeReadCutLimits limits, ZoneTreeStoreOptions policy)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxRecords < MinimumPositiveBudget || limits.MaxRecords > policy.MaximumReadCutRecords
            || limits.MaxExaminedBytes < MinimumPositiveBudget || limits.MaxExaminedBytes > policy.MaximumReadCutExaminedBytes
            || limits.MaxElapsed <= TimeSpan.Zero || limits.MaxElapsed > policy.MaximumReadCutElapsed)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLimitsMessage);
        }
    }
}
