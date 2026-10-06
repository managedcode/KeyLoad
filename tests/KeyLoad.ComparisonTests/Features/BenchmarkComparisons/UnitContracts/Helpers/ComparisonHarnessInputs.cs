using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ComparisonHarnessInputs
{
    internal static ComparisonOptions Small => new()
    {
        Documents = 16,
        Operations = 12,
        Warmup = 2,
        Repetitions = 2,
        Concurrency = 2,
        Dimensions = 8,
        TopK = 3,
        PayloadBytes = 128
    };
}
