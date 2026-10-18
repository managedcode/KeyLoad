namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Retains an actual CLR admission failure without allocating the impossible payload.</summary>
internal static class NativeRuntimeFailureFixture
{
    internal static OutOfMemoryException RuntimeOversizeFailure()
    {
        try
        {
            _ = Array.CreateInstance(typeof(byte), int.MaxValue, int.MaxValue);
        }
        catch (OutOfMemoryException failure)
        {
            return failure;
        }
        throw new InvalidOperationException("The CLR did not reject the impossible array dimensions.");
    }
}
