namespace KeyLoad.Query.Features.Search;

internal sealed record AnnSearchResult(AnnCandidate[] Candidates, AnnSearchMode Mode,
    long WorkUnits, long DistanceEvaluations, long EdgeVisits)
{
    internal long ScratchBytesUpperBound
    {
        get; init;
    }
    internal int EligibleCount
    {
        get; init;
    }
    internal int ExpansionPasses
    {
        get; init;
    }
    internal long FallbackDistanceEvaluations
    {
        get; init;
    }
}
