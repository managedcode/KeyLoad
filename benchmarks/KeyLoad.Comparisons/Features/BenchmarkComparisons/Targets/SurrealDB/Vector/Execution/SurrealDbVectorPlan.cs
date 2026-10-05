using System.Globalization;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Requires the pinned provider's actual native ANN operator and pushed predicate.</summary>
internal static class SurrealDbVectorPlan
{
    private const string Operator = "operator";
    private const string NativeKnnScan = "KnnScan";
    private const string Attributes = "attributes";
    private const string Index = "index";
    private const string Ef = "ef";
    private const string Dimension = "dimension";
    private const string Predicate = "predicate";
    private const string PrefilterTier = "prefilter_tier";
    private const string ExactTier = "exact";

    internal static void Validate(JsonElement plan, string index, int ef, int dimensions, VectorQueryMode mode)
    {
        if (!HasNativeScan(plan, index, ef, dimensions, mode))
        {
            throw new ComparisonFailureException(SurrealDbNativeTokens.TokenSurrealDbHnswPlanNotProven);
        }
    }

    private static bool HasNativeScan(JsonElement node, string index, int ef, int dimensions, VectorQueryMode mode)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty(Operator, out var operation) && operation.GetString() == NativeKnnScan)
            {
                var attributes = node.GetProperty(Attributes);
                return attributes.GetProperty(Index).GetString() == index
                    && attributes.GetProperty(Ef).GetString() == ef.ToString(CultureInfo.InvariantCulture)
                    && attributes.GetProperty(Dimension).GetString() == dimensions.ToString(CultureInfo.InvariantCulture)
                    && (!attributes.TryGetProperty(PrefilterTier, out var tier) || tier.GetString() != ExactTier)
                    && (mode == VectorQueryMode.Plain || attributes.TryGetProperty(Predicate, out var predicate)
                        && !string.IsNullOrWhiteSpace(predicate.GetString()));
            }
            return node.EnumerateObject().Any(property => HasNativeScan(property.Value, index, ef, dimensions, mode));
        }
        return node.ValueKind == JsonValueKind.Array && node.EnumerateArray().Any(child => HasNativeScan(child, index, ef, dimensions, mode));
    }
}
