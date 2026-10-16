namespace KeyLoad.Clustering;

internal static class NativeCommandMetadataAdmission
{
    private const int NoCapacity = 0;

    internal static bool Valid(NativeCommandMetadataAdmissionV1? value)
        => value is
        {
            Version: NativeCommandMetadataProtocol.CurrentVersion,
            MaximumReadBytes: > NoCapacity, MaximumExaminedRecords: > NoCapacity,
            MaximumResultBytes: > NoCapacity
        };

    internal static void Require(NativeCommandMetadataAdmissionV1? value, DatabaseLimits limits)
    {
        if (value is null || !Valid(value) || value.MaximumReadBytes > limits.MaxQueryReadBytes
            || value.MaximumExaminedRecords > limits.MaxScanRecords || value.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementReadScope.InvalidRequest);
        }
    }
}
