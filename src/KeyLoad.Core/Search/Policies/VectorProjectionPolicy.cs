using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Search;

/// <summary>Validates canonical projection effects and compares current effective field classifications.</summary>
internal static class VectorProjectionPolicy
{
    private const string InvalidPolicy = "The vector projection field policy is corrupt.";
    private const char HexNineDigit = '9';
    private const char HexLowerADigit = 'a';
    private const char HexLowerFDigit = 'f';
    private const string EffectCorrupt = "The stored vector projection effect is corrupt.";

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

    internal static void ValidateEffect(VectorProjectionEffect? effect, VectorProjectionLineage lineage,
        VectorRecord vector)
    {
        const int Sha256HexDigestCharacters = 64;
        const char HexZeroDigit = '0';

        if (effect is null || effect.Fingerprint is null || effect.Fingerprint.Length != Sha256HexDigestCharacters
            || effect.Fingerprint.Any(character => character is not (>= HexZeroDigit and <= HexNineDigit or >= HexLowerADigit and <= HexLowerFDigit))
            || effect.Receipt is null || effect.Receipt.Kind != MutationDiscriminatorNames.ApplyVectorProjection
            || effect.Receipt.Resource != lineage.TargetCollection || effect.Receipt.Id != lineage.TargetId
            || effect.Receipt.Revision != vector.DocumentRevision
            || !MatchesCanonicalVector(effect.Fingerprint, lineage.TargetCollection, vector))
        {
            throw Errors.Fail(ErrorCode.Corruption, EffectCorrupt);
        }
    }

    internal static bool MatchesCanonicalVector(string fingerprint, string collection, VectorRecord vector)
        => !vector.Values.IsDefault && vector.Values.Length == vector.Space.Dimension
            && vector.Values.All(float.IsFinite)
            && string.Equals(fingerprint, JsonData.Fingerprint(new PutVector(collection, vector.DocumentId,
                vector.Field, vector.Values, vector.Space, vector.DocumentRevision)), StringComparison.Ordinal);

    internal static bool Same(ImmutableArray<string> left, ImmutableArray<string> right)
        => !left.IsDefault && !right.IsDefault && left.SequenceEqual(right, StringComparer.Ordinal);

    internal static bool TargetIsAtLeastAsRestrictive(ImmutableArray<string> source,
        ImmutableArray<string> target)
        => !source.IsDefault && !target.IsDefault
            && source.All(classification => target.Contains(classification, StringComparer.Ordinal));
}
