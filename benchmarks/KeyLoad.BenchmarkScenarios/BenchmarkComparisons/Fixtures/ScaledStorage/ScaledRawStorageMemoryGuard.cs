namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class ScaledRawStorageMemoryGuard
{
    private const string MemoryMessage = "Observed process memory exceeded the frozen ceiling or headroom.";
    private const string UnavailablePeakMessage = "The process did not provide a positive lifetime peak memory observation.";

    internal static void ValidateObserved(long peak, long available, long ceiling, long requiredHeadroomBytes)
    {
        const int PeakValidationBoundary = 0;

        if (peak <= PeakValidationBoundary)
        {
            throw new InvalidDataException(UnavailablePeakMessage);
        }

        if (peak > ceiling || checked(peak + requiredHeadroomBytes) > available)
        {
            throw new InvalidOperationException(MemoryMessage);
        }
    }
}
