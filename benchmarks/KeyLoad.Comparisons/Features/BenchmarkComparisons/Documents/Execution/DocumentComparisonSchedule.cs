namespace KeyLoad.Comparisons;

/// <summary>One deterministic native operation and its exact input.</summary>
public sealed record DocumentScheduledOperation(Scenario Operation, BenchmarkDocument Input);
/// <summary>Disjoint mutation lanes and stable read lanes shared by every native database.</summary>
public sealed class DocumentComparisonSchedule(DocumentComparisonScenario scenario, DocumentComparisonCorpus corpus, int operations)
{
    /// <summary>Gets records added during measurement.</summary>
    public int Added => scenario is DocumentComparisonScenario.Create or DocumentComparisonScenario.Ingest ? operations : scenario == DocumentComparisonScenario.MixedCrud ? operations / DocumentMeasurementValues.MixedCrudCycleLength : DocumentMeasurementValues.NoObservedItems;
    /// <summary>Gets records deleted during measurement.</summary>
    public int Deleted => scenario == DocumentComparisonScenario.Delete ? operations : scenario == DocumentComparisonScenario.MixedCrud ? operations / DocumentMeasurementValues.MixedCrudCycleLength : DocumentMeasurementValues.NoObservedItems;
    /// <summary>Gets unique replacement identities.</summary>
    public int Updated => scenario switch
    {
        DocumentComparisonScenario.Update => operations,
        DocumentComparisonScenario.ReadUpdate50 => operations / DocumentMeasurementValues.ReadUpdate50CycleLength,
        DocumentComparisonScenario.ReadUpdate95 => (operations + DocumentMeasurementValues.ReadUpdate95CeilingAdjustment) / DocumentMeasurementValues.ReadUpdate95CycleLength,
        DocumentComparisonScenario.MixedCrud => operations / DocumentMeasurementValues.MixedCrudCycleLength,
        _ => DocumentMeasurementValues.NoObservedItems
    };
    /// <summary>Gets the initial live dataset count.</summary>
    public int Initial => scenario == DocumentComparisonScenario.Ingest ? DocumentMeasurementValues.NoObservedItems : corpus.Documents.Count;
    /// <summary>Gets exact final live cardinality.</summary>
    public int Final => Initial + Added - Deleted;
    /// <summary>Gets the exclusive upper identity bound, separate from live cardinality.</summary>
    public int MaximumIdentityExclusive => Initial + Added;
    /// <summary>Generates one bounded-index operation without retaining a plan table.</summary>
    public DocumentScheduledOperation At(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, operations);
        if (scenario == DocumentComparisonScenario.Ingest)
        {
            return new(Scenario.DocumentWrite, corpus.CreateDocument(ordinal));
        }

        if (scenario == DocumentComparisonScenario.Create)
        {
            return new(Scenario.DocumentWrite, corpus.CreateDocument(Initial + ordinal));
        }

        if (scenario == DocumentComparisonScenario.Update)
        {
            return new(Scenario.DocumentUpdate, corpus.Replacement(ordinal));
        }

        if (scenario == DocumentComparisonScenario.Delete)
        {
            return new(Scenario.DocumentDelete, corpus.CreateDocument(ordinal));
        }

        if (scenario == DocumentComparisonScenario.MixedCrud)
        {
            var position = ordinal / DocumentMeasurementValues.MixedCrudCycleLength;
            return (ordinal % DocumentMeasurementValues.MixedCrudCycleLength) switch
            {
                DocumentMeasurementValues.NoObservedItems => Read(Updated + Deleted + position % (Initial - Updated - Deleted)),
                DocumentMeasurementValues.SingleItemCount => new(Scenario.DocumentWrite, corpus.CreateDocument(Initial + position)),
                DocumentMeasurementValues.MixedCrudUpdatePosition => new(Scenario.DocumentUpdate, corpus.Replacement(position)),
                _ => new(Scenario.DocumentDelete, corpus.CreateDocument(Updated + position))
            };
        }
        if (scenario is DocumentComparisonScenario.ReadUpdate50 or DocumentComparisonScenario.ReadUpdate95)
        {
            var ratio = scenario == DocumentComparisonScenario.ReadUpdate50 ? DocumentMeasurementValues.ReadUpdate50CycleLength : DocumentMeasurementValues.ReadUpdate95CycleLength;
            if (ordinal % ratio == DocumentMeasurementValues.NoObservedItems)
            {
                return new(Scenario.DocumentUpdate, corpus.Replacement(ordinal / ratio));
            }

            return Read(Updated + Permute(ordinal, Initial - Updated));
        }
        return Read(scenario == DocumentComparisonScenario.SequentialRead ? ordinal % Initial : Permute(ordinal, Initial));
    }
    /// <summary>Enumerates the exact complete final state in native ordinal identifier order.</summary>
    public IEnumerable<BenchmarkDocument> FinalDocuments(bool initialState = false)
    {
        if (initialState)
        {
            for (var id = DocumentMeasurementValues.NoObservedItems; id < Initial; id++)
            {
                yield return corpus.CreateDocument(id);
            }

            yield break;
        }
        for (var id = DocumentMeasurementValues.NoObservedItems; id < MaximumIdentityExclusive; id++)
        {
            var deleted = scenario == DocumentComparisonScenario.Delete ? id < Deleted
                : scenario == DocumentComparisonScenario.MixedCrud && id >= Updated && id < Updated + Deleted;
            if (!deleted)
            {
                yield return id < Updated ? corpus.Replacement(id) : corpus.CreateDocument(id);
            }
        }
    }
    private DocumentScheduledOperation Read(int id) => new(Scenario.PointRead, corpus.CreateDocument(id));
    private static int Permute(int ordinal, int count)
    {
        // 7919 is coprime to both canonical corpus sizes and the fixed disjoint read-lane sizes.
        var stride = DocumentMeasurementValues.ReadPermutationStride;
        while (GreatestCommonDivisor(stride, count) != DocumentMeasurementValues.SingleItemCount)
        {
            stride += DocumentMeasurementValues.PermutationStrideIncrement;
        }

        return checked((int)(((long)ordinal * stride + DocumentComparisonContract.Current.Seed) % count));
    }
    private static int GreatestCommonDivisor(int left, int right)
    { while (right != DocumentMeasurementValues.NoObservedItems) { var remainder = left % right; left = right; right = remainder; } return left; }
}
