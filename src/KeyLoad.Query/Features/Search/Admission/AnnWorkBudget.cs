using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed record PackedAnnOptions
{
    internal int Connections { get; init; } = 16;
    internal int EfConstruction { get; init; } = 128;
    internal int EfSearch { get; init; } = 128;
    internal int MaxLevel { get; init; } = 16;
    internal int ExactThreshold { get; init; } = 256;
    internal int MaxRecords { get; init; } = 5_000_000;
    internal long MaxIndexBytes { get; init; } = 268_435_456;
    internal long MaxScratchBytes { get; init; } = 8_388_608;
    internal ulong Seed { get; init; } = 0x4B45594C4F414431UL;
}

internal sealed class AnnWorkBudget
{
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
        if (maxWorkUnits <= 0)
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
        if (units < 0)
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
        if (dimension < 1 || dimension > 4_096)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidCharge);
        }
        Charge(dimension);
        distanceEvaluations = checked(distanceEvaluations + 1);
    }

    internal void ChargeEdge()
    {
        Charge(1);
        edgeVisits = checked(edgeVisits + 1);
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
