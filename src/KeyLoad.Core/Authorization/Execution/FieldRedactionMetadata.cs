using System.Collections.Immutable;

namespace KeyLoad.Core.Authorization;

/// <summary>Discloses original omitted paths only when every overlapping policy permits schema visibility.</summary>
internal static class FieldRedactionMetadata
{
    internal static ImmutableArray<string> Visible(IAuthorizationPolicy authorization, PrincipalRecord principal,
        ResourceDefinition resource, IReadOnlyList<string> omitted)
        => omitted is { Count: 0 } ? [] : VisiblePaths(authorization, principal, resource, omitted);

    private static ImmutableArray<string> VisiblePaths(IAuthorizationPolicy authorization, PrincipalRecord principal,
        ResourceDefinition resource, IReadOnlyList<string> omitted)
        => omitted.Where(path => authorization.GetEffectiveFieldPolicies(resource, path)
            .All(policy => authorization.CanReadFieldSchema(principal, policy))).ToImmutableArray();
}
