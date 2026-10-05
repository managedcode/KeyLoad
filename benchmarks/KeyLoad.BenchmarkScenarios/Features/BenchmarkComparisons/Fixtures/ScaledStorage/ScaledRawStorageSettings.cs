namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class ScaledRawStorageSettings
{
    private const int HundredThousand = 100_000;
    private const int OneMillion = 1_000_000;
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private const int KeyBytes = 16;
    private const int ReservedMissKeyCount = 1;
    private const int OrderBytes = sizeof(int);
    private const int ScratchCopies = 3;
    private const string InvalidCountMessage = "The scaled native fixture count is unsupported.";
    private const string InvalidPayloadMessage = "The scaled native fixture payload size is unsupported.";
    private const string InsufficientMemoryMessage = "The process does not have the frozen capacity and headroom for this scaled fixture.";

    internal static void ValidateInput(int recordCount, int payloadBytes)
    {
        if (recordCount is <= 0 or > OneMillion)
        {
            throw new ArgumentOutOfRangeException(nameof(recordCount), InvalidCountMessage);
        }

        if (payloadBytes is not SmallPayloadBytes and not LargePayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadBytes), InvalidPayloadMessage);
        }
    }

    internal static long ValidateFixtureCapacity(int recordCount, int payloadBytes, ScaledStorageExecutionOptions settings)
    {
        ValidateInput(recordCount, payloadBytes);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var projectedCeiling = checked(ScaledRawStorageProcessMemory.ReadPeakBytes(process)
            + CapacityBound(recordCount, payloadBytes, settings.RequiredHeadroomBytes));
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var qualificationCount = recordCount is HundredThousand or OneMillion;
        if (qualificationCount && available < settings.MinimumQualificationCapacityBytes)
        {
            throw new InvalidOperationException(InsufficientMemoryMessage);
        }

        var ceiling = qualificationCount ? settings.MaximumProcessBytes : projectedCeiling;
        if (projectedCeiling > settings.MaximumProcessBytes || ceiling > available)
        {
            throw new InvalidOperationException(InsufficientMemoryMessage);
        }

        return ceiling;
    }

    internal static long CapacityBound(int recordCount, int payloadBytes, long requiredHeadroomBytes)
    {
        var keysAndOrder = checked(((long)recordCount + ReservedMissKeyCount) * KeyBytes
            + ((long)recordCount * OrderBytes));
        var values = checked((long)recordCount * payloadBytes);
        var scratch = checked((long)payloadBytes * ScratchCopies);
        return checked(keysAndOrder + values + scratch
            + requiredHeadroomBytes);
    }

}
