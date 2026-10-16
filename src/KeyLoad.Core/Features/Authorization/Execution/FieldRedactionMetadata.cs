using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Authorization;

/// <summary>Discloses original omitted paths only when every overlapping policy permits schema visibility.</summary>
internal static class FieldRedactionMetadata
{
    internal static ImmutableArray<string> Visible(IAuthorizationPolicy authorization, PrincipalRecord principal,
        ResourceDefinition resource, IReadOnlyList<string> omitted)
        => omitted.Where(path => authorization.GetEffectiveFieldPolicies(resource, path)
            .All(policy => authorization.CanReadFieldSchema(principal, policy))).ToImmutableArray();
}
