namespace KeyLoad;

/// <summary>Validates one immutable source-to-new-owner provenance record without rewriting source authority.</summary>
internal static class ResourceRetirementConservationOriginValidation
{
    private const int Version = 1;
    private const long Empty = 0;
    private const string Invalid = "The native retirement source origin is invalid.";

    internal static void Require(ResourceRetirementConservationSourceOriginV1 origin)
    {
        if (origin is null || origin.Version != Version || origin.SourceNodeId == Guid.Empty
            || origin.SourceIncarnation == Guid.Empty || origin.SourcePosition < Empty || origin.SourceApplied < Empty
            || origin.SourceOwner is null || origin.TargetOwner is null
            || origin.SourceIncarnation != origin.SourceOwner.Incarnation
            || origin.SourceOwner.Incarnation == origin.TargetOwner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        ResourceRetirementConservation.Initial(origin.SourceOwner);
        ResourceRetirementConservation.Initial(origin.TargetOwner);
        var summary = Summary(origin);
        summary.RequireValid();
        if (origin.SourceFloor is not { } floor)
        {
            if (summary.Records != Empty)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            return;
        }
        ResourceRetirementConservation.Validate(floor);
        if (!ResourceRetirementConservation.SameOwner(floor.PhysicalOwner, origin.SourceOwner)
            || !summary.Same(new(floor.AuthorityRecordCount, floor.AuthorityDigest)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    internal static ResourceRetirementAuthoritySummary Summary(ResourceRetirementConservationSourceOriginV1 origin)
        => new(origin.SourceAuthorityRecordCount, origin.SourceAuthorityDigest);
}
