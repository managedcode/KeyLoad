namespace KeyLoad.Security.Features.Authorization;

internal static class ReplayInputPolicy
{
    internal static void Require(PrincipalRecord principal, ResourceDefinition resource)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.FieldPolicies.Concat(resource.HeaderPolicies)
            .Any(policy => !AuthorizationPolicy.Grant(principal, policy.RawReadGrant)
                || !AuthorizationPolicy.Grant(principal, policy.RawUseGrant)))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, "Complete replay input requires every raw-read and raw-use grant.");
        }
    }
}
