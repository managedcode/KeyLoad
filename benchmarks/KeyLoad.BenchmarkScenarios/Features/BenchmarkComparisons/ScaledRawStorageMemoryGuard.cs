namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class ScaledRawStorageMemoryGuard
{
    internal const long MaximumProcessBytes = 12L * 1024 * 1024 * 1024;
    internal const long RequiredHeadroomBytes = 2L * 1024 * 1024 * 1024;
    private const string MemoryMessage = "Observed process memory exceeded the frozen ceiling or headroom.";
    private const string UnavailablePeakMessage = "The process did not provide a positive lifetime peak memory observation.";

    internal static void ValidateObserved(long peak, long available, long ceiling)
    {
        if (peak <= 0)
        {
            throw new InvalidDataException(UnavailablePeakMessage);
        }

        if (peak > ceiling || checked(peak + RequiredHeadroomBytes) > available)
        {
            throw new InvalidOperationException(MemoryMessage);
        }
    }
}
