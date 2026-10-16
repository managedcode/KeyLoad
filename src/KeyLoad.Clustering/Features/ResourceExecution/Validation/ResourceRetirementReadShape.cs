namespace KeyLoad.Clustering;

internal static class ResourceRetirementReadShape
{
    internal static void Require(ResourceRetirementReadRequestV1 query, PrincipalRecord principal,
        ReplicaSiloDiscovery actual, DatabaseLimits limits, TimeProvider clock, DateTimeOffset originalExpiry)
    {
        if (query is null || query.Version != ResourceRetirementReadContract.Version || query.ObservedOwner?.Owner is null
            || query.ObservedOwner.Owner.VoterIds.IsDefaultOrEmpty
            || query.OriginalPolicyEpoch < ResourceRetirementReadContract.MinimumPolicyEpoch || query.ExpiresAt != originalExpiry
            || query.MaximumReadBytes <= ResourceRetirementReadContract.NoReadBytes || query.MaximumReadBytes > limits.MaxQueryReadBytes
            || query.MaximumExaminedRecords <= ResourceRetirementReadContract.NoRecords || query.MaximumExaminedRecords > limits.MaxScanRecords
            || query.MaximumResultBytes <= ResourceRetirementReadContract.NoRecords || query.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }
        ResourceRetirementReadActionShape.Require(query, limits);
        if (query.Operation != ResourceRetirementReadOperation.DropParent
            && (query.Request is null || query.Request.Version != ResourceRetirementReadContract.Version
                || query.Request.RetirementId == Guid.Empty))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
        }
        if (principal.PolicyEpoch != query.OriginalPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementReadScope.InvalidRequest);
        }
        if (query.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementReadScope.InvalidRequest);
        }
        if (!actual.TransportReady || actual.VoterId != query.DestinationVoter
            || actual.SiloAddress != query.DestinationSiloAddress
            || actual.Incarnation != query.ObservedOwner.Owner.Incarnation
            || !query.ObservedOwner.Owner.VoterIds.Contains(actual.VoterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementReadScope.InvalidRequest);
        }
    }
}
