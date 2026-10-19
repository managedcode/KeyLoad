using System.Runtime.InteropServices;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class SerializationBenchmarkOwnershipTests
{
    [Test]
    [Arguments(1024)]
    [Arguments(16384)]
    public async Task AcIsPerf001DecodedRawBuffersAreIndependentOfBothEncodedInputsAndOriginalCorpus(int size)
    {
        var state = new SerializationBenchmarkState<KeyLoad.Storage.StorageMutation[]>(SerializationBenchmarkCorpus.Storage(size));
        var execution = state.RuntimeDecode();
        var json = state.JsonDecode();
        var expected = JsonDefaults.Serialize(state.Input);
        state.RuntimeBytes.AsSpan().Clear();
        state.JsonBytes.AsSpan().Clear();
        foreach (var mutation in state.Input)
        {
            Clear(mutation.Key);
            if (mutation.Value is { } value)
            {
                Clear(value);
            }
        }
        await Assert.That(JsonDefaults.Serialize(execution)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(JsonDefaults.Serialize(json)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        Clear(execution[0].Value!.Value);
        await Assert.That(JsonDefaults.Serialize(json)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcIsPerf001InvalidSizeAndUninitializedFixtureFailBeforeTiming()
    {
        var fixture = new CommandSerializationBenchmarks { PayloadBytes = 0 };
        await Assert.That(fixture.Setup).Throws<ArgumentOutOfRangeException>();
        await Assert.That(fixture.RuntimeDecode).Throws<InvalidOperationException>();
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
