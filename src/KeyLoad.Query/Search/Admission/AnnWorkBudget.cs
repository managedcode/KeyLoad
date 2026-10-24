using KeyLoad.Core;

namespace KeyLoad.Query.Search;

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
    private long writtenBytes;

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

    internal long WrittenBytes => Interlocked.Read(ref writtenBytes);

    internal void RecordWritten(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        Interlocked.Exchange(ref writtenBytes, checked(Interlocked.Read(ref writtenBytes) + bytes));
    }

    internal long WorkUnits => Interlocked.Read(ref workUnits);
    internal long DistanceEvaluations => Interlocked.Read(ref distanceEvaluations);
    internal long EdgeVisits => Interlocked.Read(ref edgeVisits);

    internal void Check() => readBudget.Check();

    // Charges only count work: cancellation and the deadline are checked when a build or search starts and before
    // it returns, because reading the clock for every edge and distance took most of a build's time.
    internal void Charge(long units)
    {
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
