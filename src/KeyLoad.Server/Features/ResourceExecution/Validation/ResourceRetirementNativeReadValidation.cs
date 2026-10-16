using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementNativeReadValidation
{
    internal static void RequireRequest(ResourceRetirementNativeRequest outer, ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
        => ResourceRetirementNativeReadRequestValidation.Require(outer, query, limits);

    internal static void RequireReply(ResourceRetirementReadRequestV1 query, ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
        => ResourceRetirementNativeReadReplyValidation.Require(query, reply, limits);
}
