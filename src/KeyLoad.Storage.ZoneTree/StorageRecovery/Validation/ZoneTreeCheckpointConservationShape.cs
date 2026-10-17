namespace KeyLoad.Storage.ZoneTree;

/// <summary>Checks required current-format fields before any checkpoint data callback.</summary>
internal static class ZoneTreeCheckpointConservationShape
{
    private const int DigestBytes = 32;
    private const long EmptyRecords = 0;
    private const string Invalid = "The native checkpoint retirement conservation proof is invalid.";

    internal static void Require(ZoneTreeCheckpointMetadata metadata)
    {
        if (metadata.ConservationAuthorityRecords < EmptyRecords
            || metadata.ConservationAuthorityDigest.Length != DigestBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var summary = new ResourceRetirementAuthoritySummary(metadata.ConservationAuthorityRecords,
            metadata.ConservationAuthorityDigest);
        summary.RequireValid();
        if (metadata.ConservationFloor is { } floor)
        {
            ResourceRetirementConservation.Validate(floor);
            if (floor.PhysicalOwner.Incarnation != metadata.Incarnation)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            if (!summary.Same(new(floor.AuthorityRecordCount, floor.AuthorityDigest)))
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
        }
        else if (summary.Records != EmptyRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    internal static void RequireCount(ZoneTreeCheckpointMetadata metadata, long totalRecords)
    {
        if (metadata.ConservationAuthorityRecords > totalRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }
}
