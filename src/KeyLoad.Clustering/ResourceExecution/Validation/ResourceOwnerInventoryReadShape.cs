namespace KeyLoad.Clustering;

/// <summary>Admits only the closed native read shape; canonical inventory authority remains in Core.</summary>
internal static class ResourceOwnerInventoryReadShape
{
    private const int Version = 1;
    private const int Empty = 0;
    private const long EmptyBytes = 0;
    private const long MinimumPolicyEpoch = 0;

    internal static void Require(ResourceOwnerInventoryReadRequestV1 query, PrincipalRecord principal,
        SiloStorageIdentityProvider discovery, DatabaseLimits limits, TimeProvider clock)
    {
        if (query is null || principal is null || query.Version != Version || query.Request is null
            || query.Request.Version != Version || query.ObservedOwner is null || query.ObservedOwner.Owner is null
            || query.ObservedOwner.Owner.VoterIds.IsDefaultOrEmpty
            || string.IsNullOrWhiteSpace(query.DestinationVoter) || string.IsNullOrWhiteSpace(query.DestinationSiloAddress)
            || query.OriginalPolicyEpoch < MinimumPolicyEpoch
            || query.MaximumReadBytes <= EmptyBytes || query.MaximumReadBytes > limits.MaxQueryReadBytes
            || query.MaximumExaminedRecords <= Empty || query.MaximumExaminedRecords > limits.MaxScanRecords
            || query.MaximumResultBytes <= Empty || query.MaximumResultBytes > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceOwnerInventoryReadScope.InvalidRequest);
        }
        RequireAction(query);
        if (principal.PolicyEpoch != query.OriginalPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryReadScope.InvalidRequest);
        }
        if (query.Request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryReadScope.InvalidRequest);
        }
        RequireNativeLocal(query, discovery.Read());
    }

    private static void RequireAction(ResourceOwnerInventoryReadRequestV1 query)
    {
        var valid = query.Operation switch
        {
            ResourceOwnerInventoryReadOperation.CaptureControl => query.Control is null
                && query.Continuation is null && query.OwnerCut is null,
            ResourceOwnerInventoryReadOperation.ReadPage => query.Control is not null && query.OwnerCut is null,
            ResourceOwnerInventoryReadOperation.ValidateControl => query.Control is not null
                && query.Continuation is null && query.OwnerCut is null,
            ResourceOwnerInventoryReadOperation.ValidateOwner => query.Control is not null
                && query.OwnerCut is not null && query.Continuation is null,
            _ => false
        };
        if (!valid || query.Control is { } control && control.Request != query.Request)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceOwnerInventoryReadScope.InvalidRequest);
        }
    }

    private static void RequireNativeLocal(ResourceOwnerInventoryReadRequestV1 query, SiloStorageIdentity actual)
    {
        if (string.IsNullOrEmpty(actual.SiloAddress) || actual.SiloName != query.DestinationVoter
            || actual.SiloAddress != query.DestinationSiloAddress
            || actual.Incarnation != query.ObservedOwner.Owner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryReadScope.InvalidRequest);
        }
    }
}
