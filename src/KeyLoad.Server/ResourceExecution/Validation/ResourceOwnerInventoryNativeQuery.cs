using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceOwnerInventoryNativeQuery
{
    private const long UnassignedPolicyEpoch = 0;
    private const long EmptyReadBytes = 0;
    private const int EmptyRecords = 0;
    internal static void RequireRequest(ResourceOwnerInventoryNativeRequest outer, ResourceOwnerInventoryReadRequestV1 query)
    {
        if (query is null || query.Version != ResourceOwnerInventoryNativeProtocol.Version
            || query.Request is null || query.Request.Version != outer.Version
            || query.Request.ExpiresAt != outer.ExpiresAt || query.OriginalPolicyEpoch != UnassignedPolicyEpoch
            || query.DestinationVoter != outer.DestinationVoter || query.DestinationSiloAddress != outer.DestinationSiloAddress
            || !PhysicalOwnerEntryValidation.Valid(query.ObservedOwner)
            || !PhysicalOwnerEntryValidation.Same(query.ObservedOwner, outer.Destination)
            || query.MaximumReadBytes != outer.MaximumReadBytes
            || query.MaximumExaminedRecords != outer.MaximumExaminedRecords
            || query.MaximumResultBytes != outer.MaximumResultBytes
            || query.Control is { } control && (control.Request != query.Request
                || control.PrincipalId != outer.PrincipalId || control.PolicyEpoch != outer.PolicyEpoch))
        {
            throw Invalid();
        }
        var valid = query.Operation switch
        {
            ResourceOwnerInventoryReadOperation.CaptureControl => query.Control is null
                && query.Continuation is null && query.OwnerCut is null,
            ResourceOwnerInventoryReadOperation.ReadPage => query.Control is not null && query.OwnerCut is null,
            ResourceOwnerInventoryReadOperation.ValidateControl => query.Control is not null
                && query.Continuation is null && query.OwnerCut is null,
            ResourceOwnerInventoryReadOperation.ValidateOwner => query.Control is not null
                && query.Continuation is null && query.OwnerCut is not null,
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
    }

    internal static void RequireReply(ResourceOwnerInventoryReadRequestV1 query, ResourceOwnerInventoryReadReplyV1 reply,
        string sourcePrincipalId, long sourcePolicyEpoch)
    {
        if (reply is null || reply.ReadBytes < EmptyReadBytes || reply.ReadBytes > query.MaximumReadBytes
            || reply.ExaminedRecords < EmptyRecords || reply.ExaminedRecords > query.MaximumExaminedRecords)
        {
            throw Invalid();
        }
        var valid = query.Operation switch
        {
            ResourceOwnerInventoryReadOperation.CaptureControl => reply.Control is { } control
                && control.PrincipalId == sourcePrincipalId && control.PolicyEpoch == sourcePolicyEpoch
                && control.Request == query.Request && reply.Page is null && !reply.Validated,
            ResourceOwnerInventoryReadOperation.ReadPage => reply.Control is null && reply.Page is not null && !reply.Validated,
            ResourceOwnerInventoryReadOperation.ValidateControl or ResourceOwnerInventoryReadOperation.ValidateOwner
                => reply.Control is null && reply.Page is null && reply.Validated,
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceOwnerInventoryNativeProtocol.InvalidProof);
}
