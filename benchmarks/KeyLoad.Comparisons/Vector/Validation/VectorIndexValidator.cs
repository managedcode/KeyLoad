namespace KeyLoad.Comparisons;

/// <summary>Checks the actual native index receipt and query plan without initializing or measuring a database.</summary>
internal static class VectorIndexValidator
{
    internal const int FirstIndex = 0;
    internal const string NativeIndexReceiptDoesNotMatch = "Native index receipt does not match the selected profile.";
    internal const string NativeQueryPlanIsEmpty = "Native query plan is empty.";
    internal const string TheMeasuredPostgreSQLQueryPlanDoes = "The measured PostgreSQL query plan does not prove the requested ANN index is used.";
    internal const string NativeANNDidNotProvideItsActual = "NativeANN did not provide its actual algorithm configuration.";
    internal static void Require(VectorComparisonProfile profile, VectorIndexReceipt index, string plan)
    {
        if (index.IndexKind != profile.IndexKind || !double.IsFinite(index.BuildMilliseconds) || index.BuildMilliseconds < FirstIndex
            || (profile.IndexKind == VectorIndexKind.Exact && index.BuildMilliseconds != FirstIndex)
            || (profile.IndexKind != VectorIndexKind.Exact && index.BuildMilliseconds <= FirstIndex))
        {
            throw new InvalidDataException(NativeIndexReceiptDoesNotMatch);
        }

        if (string.IsNullOrWhiteSpace(plan))
        {
            throw new InvalidDataException(NativeQueryPlanIsEmpty);
        }

        if (profile.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat
            && !plan.Contains(profile.IndexKind == VectorIndexKind.Hnsw ? VectorProfileTokens.HnswMethod : VectorProfileTokens.IvfFlatMethod, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(TheMeasuredPostgreSQLQueryPlanDoes);
        }

        if (profile.IndexKind == VectorIndexKind.NativeAnn && string.IsNullOrWhiteSpace(index.Definition))
        {
            throw new InvalidDataException(NativeANNDidNotProvideItsActual);
        }

    }

}
