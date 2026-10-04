using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Search;

/// <summary>Captures and compares the current effective classifications of projection fields.</summary>
internal static class VectorProjectionPolicy
{
    private const string InvalidPolicy = "The vector projection field policy is corrupt.";

    internal static ImmutableArray<string> Classifications(IAuthorizationPolicy authorization,
        ResourceDefinition resource, string path)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var policy in authorization.GetEffectiveFieldPolicies(resource, path))
        {
            if (string.IsNullOrEmpty(policy.Classification))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidPolicy);
            }
            classes.Add(policy.Classification);
        }
        var result = classes.ToArray();
        Array.Sort(result, StringComparer.Ordinal);
        return [.. result];
    }

    internal static bool Same(ImmutableArray<string> left, ImmutableArray<string> right)
        => !left.IsDefault && !right.IsDefault && left.SequenceEqual(right, StringComparer.Ordinal);

    internal static bool TargetIsAtLeastAsRestrictive(ImmutableArray<string> source,
        ImmutableArray<string> target)
        => !source.IsDefault && !target.IsDefault
            && source.All(classification => target.Contains(classification, StringComparer.Ordinal));
}
