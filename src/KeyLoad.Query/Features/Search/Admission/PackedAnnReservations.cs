namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnReservations
{
    private const int MinimumPositiveCount = 1;
    private const int InitialSequence = 0;
    private const int AlignmentRemainderMask = 7;
    private const long AlignedByteMask = ~7L;
    private const int ArrayObjectAllowance = 64;
    private const long Utf16CharacterBytes = 2L;
    private const long TerminatorCharacterCount = 1L;
    private const int HeapScratchArrayCount = 4;
    private const int CandidateDescriptorBytes = 32;

    internal const long FixedRetainedBytes = 512;
    private const long FixedScratchBytes = 1_024;
    private const string ResourceExceeded = "The packed ANN resource bound is exceeded.";

    internal static long Array(int width, long length)
    {
        if (width < MinimumPositiveCount || length < InitialSequence || length > System.Array.MaxLength)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
        try
        {
            var bytes = checked((long)width * length);
            var aligned = checked((bytes + AlignmentRemainderMask) & AlignedByteMask);
            return checked(ArrayObjectAllowance + aligned);
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
            var bytes = checked(Utf16CharacterBytes * (length + TerminatorCharacterCount));
            var aligned = checked((bytes + AlignmentRemainderMask) & AlignedByteMask);
            return checked(ArrayObjectAllowance + aligned);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }

    internal static long BuildScratch(int count, int ef, int degree)
    {
        var neighbors = checked(degree + TerminatorCharacterCount);
        return checked(FixedScratchBytes + Array(sizeof(int), count) + HeapScratchArrayCount * Array(sizeof(int), ef)
            + Array(sizeof(double), ef) + Array(sizeof(int), neighbors)
            + Array(sizeof(double), neighbors) + Array(sizeof(int), degree) + Array(sizeof(int), degree));
    }

    internal static long InitialQueryScratch(int dimension, int bitmapWords, bool hasEligibility)
        => checked(FixedScratchBytes + Array(sizeof(float), dimension)
            + (hasEligibility ? Array(sizeof(ulong), bitmapWords) : InitialSequence));

    internal static long ExactQueryScratch(long initialBytes, int candidateCount)
        => checked(initialBytes + Array(CandidateDescriptorBytes, candidateCount));

    internal static long ApproximateQueryScratch(long initialBytes, int visitWords, int ef,
        int candidateCount)
        => checked(initialBytes + Array(sizeof(ulong), visitWords) + HeapScratchArrayCount * Array(sizeof(int), ef)
            + Array(sizeof(double), ef) + Array(CandidateDescriptorBytes, candidateCount));

    internal static void RequireScratch(long bytes, long maximum)
    {
        if (bytes > maximum)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }
}
