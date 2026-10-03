using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

[NotInParallel]
internal sealed class NativeWireSupportedScalarAllocationTests
{
    private const int Iterations = 4096;

    [Test]
    public async Task AcIsPerf005WarmedNormalizedTerminalRequireAllocatesNoAuxiliaryBytes()
    {
        var types = NativeWireSupportedScalarFixtures.TerminalTypes;
        for (var warm = 0; warm < 32; warm++)
        {
            NativeWireSupported.Require(null);
            foreach (var type in types)
            {
                NativeWireSupported.Require(type);
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            NativeWireSupported.Require(null);
            foreach (var type in types)
            {
                NativeWireSupported.Require(type);
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task AcIsPerf005NullableRequireMatchesUnchangedNormalizeAllocation()
    {
        var types = NativeWireSupportedScalarFixtures.NullableTypes;
        Type? lastNormalized = null;
        for (var warm = 0; warm < 32; warm++)
        {
            foreach (var type in types)
            {
                lastNormalized = NativeWireSchema.Normalize(type);
                NativeWireSupported.Require(type);
            }
        }

        var normalizeBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            foreach (var type in types)
            {
                lastNormalized = NativeWireSchema.Normalize(type);
            }
        }
        var normalizeAllocated = GC.GetAllocatedBytesForCurrentThread() - normalizeBefore;

        for (var warm = 0; warm < 32; warm++)
        {
            foreach (var type in types)
            {
                NativeWireSupported.Require(type);
            }
        }
        var requireBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            foreach (var type in types)
            {
                NativeWireSupported.Require(type);
            }
        }
        var requireAllocated = GC.GetAllocatedBytesForCurrentThread() - requireBefore;

        await Assert.That(lastNormalized).IsEqualTo(typeof(DateTimeOffset));
        await Assert.That(requireAllocated).IsEqualTo(normalizeAllocated);
    }
}
