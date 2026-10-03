using System.Diagnostics;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Checks genuine current-process peak-memory observations and rejection boundaries.</summary>
internal sealed class ScaledRawStorageProcessMemoryTests
{
    private const int AllocationBytes = 8 * 1024 * 1024;
    private const int InvalidDataBytes = 0;
    private const int InvalidNegativePeakBytes = -1;
    private const long UnboundedAvailableBytes = long.MaxValue;
    private const long UnboundedCeilingBytes = long.MaxValue;

    [Test]
    public async Task AcScaleRt002And003ReadsPositiveByteScaleCurrentProcessPeakMonotonically()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var initialPeakBytes = ScaledRawStorageProcessMemory.ReadPeakBytes(currentProcess);
        var touchedPages = new byte[AllocationBytes];
        var pageSizeBytes = Environment.SystemPageSize;

        for (var offset = 0; offset < touchedPages.Length; offset += pageSizeBytes)
        {
            touchedPages[offset] = byte.MaxValue;
        }

        var finalPeakBytes = ScaledRawStorageProcessMemory.ReadPeakBytes(currentProcess);
        GC.KeepAlive(touchedPages);

        await Assert.That(initialPeakBytes).IsGreaterThan(0L);
        await Assert.That(initialPeakBytes).IsGreaterThanOrEqualTo(AllocationBytes);
        await Assert.That(finalPeakBytes).IsGreaterThan(0L);
        await Assert.That(finalPeakBytes).IsGreaterThanOrEqualTo(initialPeakBytes);
        await Assert.That(finalPeakBytes).IsGreaterThanOrEqualTo(AllocationBytes);
    }

    [Test]
    [Arguments(InvalidDataBytes)]
    [Arguments(InvalidNegativePeakBytes)]
    public async Task AcScaleRt002MemoryGuardRejectsUnavailableOrNonpositivePeakAsInvalidData(int peakBytes)
    {
        await Assert.That(() => ScaledRawStorageMemoryGuard.ValidateObserved(
                peakBytes,
                UnboundedAvailableBytes,
                UnboundedCeilingBytes))
            .Throws<InvalidDataException>();
    }
}
