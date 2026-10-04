namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnReservations
{
    internal const long FixedRetainedBytes = 512;
    private const long FixedScratchBytes = 1_024;
    private const string ResourceExceeded = "The packed ANN resource bound is exceeded.";

    internal static long Array(int width, long length)
    {
        if (width < 1 || length < 0 || length > System.Array.MaxLength)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
        try
        {
            var bytes = checked((long)width * length);
            var aligned = checked((bytes + 7) & ~7L);
            return checked(64 + aligned);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }

    internal static long String(int length)
    {
        try
        {
            var bytes = checked(2L * (length + 1L));
            var aligned = checked((bytes + 7) & ~7L);
            return checked(64 + aligned);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }

    internal static long BuildScratch(int count, int ef, int degree)
    {
        var neighbors = checked(degree + 1L);
        return checked(FixedScratchBytes + Array(sizeof(int), count) + 4 * Array(sizeof(int), ef)
            + Array(sizeof(double), ef) + Array(sizeof(int), neighbors)
            + Array(sizeof(double), neighbors) + Array(sizeof(int), degree) + Array(sizeof(int), degree));
    }

    internal static long InitialQueryScratch(int dimension, int bitmapWords, bool hasEligibility)
        => checked(FixedScratchBytes + Array(sizeof(float), dimension)
            + (hasEligibility ? Array(sizeof(ulong), bitmapWords) : 0));

    internal static long ExactQueryScratch(long initialBytes, int candidateCount)
        => checked(initialBytes + Array(32, candidateCount));

    internal static long ApproximateQueryScratch(long initialBytes, int visitWords, int ef,
        int candidateCount)
        => checked(initialBytes + Array(sizeof(ulong), visitWords) + 4 * Array(sizeof(int), ef)
            + Array(sizeof(double), ef) + Array(32, candidateCount));

    internal static void RequireScratch(long bytes, long maximum)
    {
        if (bytes > maximum)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }
}
