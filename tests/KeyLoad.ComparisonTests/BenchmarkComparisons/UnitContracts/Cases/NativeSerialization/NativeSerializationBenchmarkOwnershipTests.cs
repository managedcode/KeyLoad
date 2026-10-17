using System.Runtime.InteropServices;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeSerializationBenchmarkOwnershipTests
{
    [Test]
    [Arguments(1024)]
    [Arguments(16384)]
    public async Task AcIsPerf001DecodedRawBuffersAreIndependentOfBothEncodedInputsAndOriginalCorpus(int size)
    {
        var state = new NativeSerializationBenchmarkState<KeyLoad.Storage.StorageMutation[]>(NativeSerializationBenchmarkCorpus.Storage(size));
        var native = state.NativeDecode();
        var json = state.JsonDecode();
        var expected = JsonDefaults.Serialize(state.Input);
        state.NativeBytes.AsSpan().Clear();
        state.JsonBytes.AsSpan().Clear();
        foreach (var mutation in state.Input)
        {
            Clear(mutation.Key);
            if (mutation.Value is { } value)
            {
                Clear(value);
            }
        }
        await Assert.That(JsonDefaults.Serialize(native)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(JsonDefaults.Serialize(json)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        Clear(native[0].Value!.Value);
        await Assert.That(JsonDefaults.Serialize(json)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcIsPerf001InvalidSizeAndUninitializedFixtureFailBeforeTiming()
    {
        var fixture = new NativeCommandSerializationBenchmarks { PayloadBytes = 0 };
        await Assert.That(fixture.Setup).Throws<ArgumentOutOfRangeException>();
        await Assert.That(fixture.NativeDecode).Throws<InvalidOperationException>();
        fixture.PayloadBytes = 1024;
        fixture.Setup();
        try
        {
            await Assert.That(fixture.Setup).Throws<InvalidOperationException>();
        }
        finally
        {
            fixture.Cleanup();
            fixture.Cleanup();
        }
    }

    private static void Clear(ReadOnlyMemory<byte> memory)
    {
        if (!MemoryMarshal.TryGetArray(memory, out var segment))
        {
            throw new InvalidOperationException("The real corpus memory is not array-backed.");
        }
        segment.AsSpan().Clear();
    }
}
