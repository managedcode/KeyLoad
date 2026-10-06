using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class AnnWorkBudget
{
    private const int InitialSequence = 0;
    private const int MinimumPositiveCount = 1;
    private const int MaximumVectorDimension = 4_096;
    private const int AdjacentElementOffset = 1;
    private const int SingleWorkUnit = 1;

    private const string InvalidMaximum = "The ANN work maximum must be positive.";
    private const string InvalidCharge = "The ANN work charge is invalid.";
    private const string WorkExceeded = "The ANN work budget is exceeded.";
    private readonly ReadExecutionBudget readBudget;
    private readonly long maxWorkUnits;
    private long workUnits;
    private long distanceEvaluations;
    private long edgeVisits;

    internal AnnWorkBudget(ReadExecutionBudget readBudget, long maxWorkUnits)
    {
        ArgumentNullException.ThrowIfNull(readBudget);
        if (maxWorkUnits <= InitialSequence)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMaximum);
        }
        this.readBudget = readBudget;
        this.maxWorkUnits = maxWorkUnits;
    }

    internal long WorkUnits => Interlocked.Read(ref workUnits);
    internal long DistanceEvaluations => Interlocked.Read(ref distanceEvaluations);
    internal long EdgeVisits => Interlocked.Read(ref edgeVisits);

    internal void Check() => readBudget.Check();

    internal void Charge(long units)
    {
        Check();
        if (units < InitialSequence)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidCharge);
        }
        if (units > maxWorkUnits - workUnits)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, WorkExceeded);
        }
        workUnits = checked(workUnits + units);
    }

    internal void ChargeDistance(int dimension)
    {
        if (dimension < MinimumPositiveCount || dimension > MaximumVectorDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidCharge);
        }
        Charge(dimension);
        distanceEvaluations = checked(distanceEvaluations + AdjacentElementOffset);
    }

    internal void ChargeEdge()
    {
        Charge(SingleWorkUnit);
        edgeVisits = checked(edgeVisits + AdjacentElementOffset);
    }
}

internal enum AnnSearchMode
{
    ExactSmallSet,
    Approximate,
    ExactAfterInsufficientCandidates
}

internal readonly record struct AnnCandidate(int SourceOrdinal, string DocumentId, long DocumentRevision, double Score);

internal sealed record AnnSearchResult(AnnCandidate[] Candidates, AnnSearchMode Mode,
    long WorkUnits, long DistanceEvaluations, long EdgeVisits)
{
    internal long ScratchBytesUpperBound { get; init; }
    internal int EligibleCount { get; init; }
    internal int ExpansionPasses { get; init; }
    internal long FallbackDistanceEvaluations { get; init; }
}
