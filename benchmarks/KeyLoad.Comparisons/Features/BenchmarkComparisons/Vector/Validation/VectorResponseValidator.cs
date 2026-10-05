namespace KeyLoad.Comparisons;

/// <summary>Validates native vector responses against an independent canonical exact reference.</summary>
public static class VectorResponseValidator
{
    /// <summary>Validates native neighbor identity, eligibility, ordering and exact accuracy.</summary>
    /// <param name="corpus">The immutable canonical corpus.</param>
    /// <param name="actual">The actual native top-k response.</param>
    /// <param name="expected">The independent exact reference neighbors.</param>
    /// <returns>Recall against the independent exact oracle.</returns>
    public static double CalculateRecall(VectorComparisonCorpus corpus, IReadOnlyList<VectorNeighbor> actual,
        IReadOnlyList<VectorNeighbor> expected)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        var profile = corpus.Profile;
        var expectedCount = Math.Min(profile.TopK, expected.Count);
        if (expectedCount == VectorResponseValidatorValues.FirstIndex || expected.Count > profile.TopK || actual.Count != expectedCount)
        {
            throw new InvalidDataException(VectorResponseValidatorValues.AVectorResponseWasUnderfilledOr);
        }

        ValidateNative(corpus, actual);
        var matching = VectorResponseValidatorValues.FirstIndex;
        foreach (var item in actual)
        {
            foreach (var reference in expected)
            {
                if (item.Id == reference.Id)
                {
                    matching++;
                    break;
                }
            }
        }

        var recall = (double)matching / expectedCount;
        if (profile.IndexKind == VectorIndexKind.Exact && recall != VectorResponseValidatorValues.ExactRecall)
        {
            throw new InvalidDataException(VectorResponseValidatorValues.AnExactVectorResponseDiffersFrom);
        }
        return recall;
    }

    private static void ValidateNative(VectorComparisonCorpus corpus, IReadOnlyList<VectorNeighbor> actual)
    {
        VectorNeighbor? previous = null;
        for (var index = VectorResponseValidatorValues.FirstIndex; index < actual.Count; index++)
        {
            var neighbor = actual[index];
            ValidateIdentity(corpus, neighbor);
            if (previous is not null && (neighbor.Distance < previous.Distance
                || (neighbor.Distance == previous.Distance && string.CompareOrdinal(neighbor.Id, previous.Id) < VectorResponseValidatorValues.FirstIndex)))
            {
                throw new InvalidDataException(VectorResponseValidatorValues.ANativeVectorResponseIsNot);
            }
            for (var prior = VectorResponseValidatorValues.FirstIndex; prior < index; prior++)
            {
                if (actual[prior].Id == neighbor.Id)
                {
                    throw new InvalidDataException(VectorResponseValidatorValues.ANativeVectorResponseContainsDuplicate);
                }
            }
            previous = neighbor;
        }
    }

    private static void ValidateIdentity(VectorComparisonCorpus corpus, VectorNeighbor neighbor)
    {
        if (neighbor is null || string.IsNullOrEmpty(neighbor.Id) || !double.IsFinite(neighbor.Distance)
            || neighbor.Distance < -VectorResponseValidatorValues.CosineTolerance || neighbor.Distance > VectorResponseValidatorValues.MaximumCosineWithTolerance)
        {
            throw new InvalidDataException(VectorResponseValidatorValues.ANativeVectorResponseContainsMissing);
        }
        if (neighbor.Id.Length != VectorResponseValidatorValues.IdCharacterCount || neighbor.Id[VectorResponseValidatorValues.FirstIndex] != VectorResponseValidatorValues.VectorIdPrefixCharacter
            || !int.TryParse(neighbor.Id.AsSpan(VectorResponseValidatorValues.SingleElementOffset), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
            || (uint)number >= (uint)corpus.Profile.RecordCount || !corpus.Eligible(number))
        {
            throw new InvalidDataException(VectorResponseValidatorValues.ANativeVectorResponseIncludedAn);
        }
    }
}
