using System.Runtime.CompilerServices;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

[NotInParallel]
internal sealed class WireSupportedScalarAllocationTests
{
    private const int Iterations = 4096;

    [Test]
    public async Task AcIsPerf005WarmedNormalizedTerminalRequireAllocatesNoAuxiliaryBytes()
    {
        var types = WireSupportedScalarFixtures.TerminalTypes;
        for (var warm = 0; warm < 32; warm++)
        {
            WireSupported.Require(null);
            foreach (var type in types)
            {
                WireSupported.Require(type);
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            WireSupported.Require(null);
            foreach (var type in types)
            {
                WireSupported.Require(type);
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task AcIsPerf005NullableRequireMatchesUnchangedNormalizeAllocation()
    {
        var types = WireSupportedScalarFixtures.NullableTypes;
        for (var warm = 0; warm < 32; warm++)
        {
            _ = MeasureNormalized(types);
            _ = MeasureRequired(types);
        }

        var normalized = MeasureNormalized(types);
        var requireAllocated = MeasureRequired(types);

        await Assert.That(normalized.LastNormalized).IsEqualTo(typeof(DateTimeOffset));
        await Assert.That(requireAllocated).IsEqualTo(normalized.AllocatedBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (long AllocatedBytes, Type? LastNormalized) MeasureNormalized(Type[] types)
    {
        Type? lastNormalized = null;
        var normalizeBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            foreach (var type in types)
            {
                lastNormalized = WireSchema.Normalize(type);
            }
        }
        return (GC.GetAllocatedBytesForCurrentThread() - normalizeBefore, lastNormalized);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureRequired(Type[] types)
    {
        var requireBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            foreach (var type in types)
            {
                WireSupported.Require(type);
            }
        }
        return GC.GetAllocatedBytesForCurrentThread() - requireBefore;
    }
}
