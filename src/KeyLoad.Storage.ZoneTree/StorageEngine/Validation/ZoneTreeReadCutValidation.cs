namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeReadCutValidation
{
    private const string InvalidLimitsMessage = "The execution read-cut limits are invalid.";

    internal static void Validate(ZoneTreeReadCutLimits limits, ZoneTreeStoreOptions policy)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxRecords < 1 || limits.MaxRecords > policy.MaximumReadCutRecords
            || limits.MaxExaminedBytes < 1 || limits.MaxExaminedBytes > policy.MaximumReadCutExaminedBytes
            || limits.MaxElapsed <= TimeSpan.Zero || limits.MaxElapsed > policy.MaximumReadCutElapsed)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLimitsMessage);
        }
    }
}
