using System.Text;
using System.Text.Json;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class SerializationBenchmarkCorpusTests
{
    [Test]
    [Arguments(1024)]
    [Arguments(16384)]
    public async Task AcIsPerf001DocumentMethodsPreserveExactUnicodeJsonAndMetadata(int size)
    {
        var expected = SerializationBenchmarkCorpus.Document(size);
        var fixture = new DocumentSerializationBenchmarks { PayloadBytes = size };
        try
        {
            fixture.Setup();
            await AssertSame(expected, fixture.RuntimeDecode());
            await AssertSame(expected, fixture.JsonDecode());
            await AssertSame(expected, OrleansSerialization.Deserialize<DocumentResult>(fixture.RuntimeEncode()));
            await AssertSame(expected, JsonDefaults.Deserialize<DocumentResult>(fixture.JsonEncode()));
            await Assert.That(Encoding.UTF8.GetByteCount(expected.Json)).IsEqualTo(size);
            using var document = JsonDocument.Parse(expected.Json);
            await Assert.That(document.RootElement.GetProperty(SerializationReportFields.Text).GetString()).IsEqualTo("Київ🌍");
            await Assert.That(document.RootElement.GetProperty(SerializationReportFields.Number).GetRawText()).IsEqualTo("1.2300");
            await Assert.That(expected.Redacted && expected.RedactedFields.Length == 2).IsTrue();
        }
        finally
        {
            fixture.Cleanup();
        }
        await Assert.That(fixture.RuntimeDecode).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(1024)]
    [Arguments(16384)]
    public async Task AcIsPerf001CommandMethodsPreservePolymorphismVectorsAndOptionals(int size)
    {
        var expected = SerializationBenchmarkCorpus.Command(size);
        var fixture = new CommandSerializationBenchmarks { PayloadBytes = size };
        try
        {
            fixture.Setup();
            await AssertSame(expected, fixture.RuntimeDecode());
            await AssertSame(expected, fixture.JsonDecode());
            await AssertSame(expected, OrleansSerialization.Deserialize<CommandRequest>(fixture.RuntimeEncode()));
            await AssertSame(expected, JsonDefaults.Deserialize<CommandRequest>(fixture.JsonEncode()));
            var first = (PutDocument)expected.Mutations[0];
            var second = (PutDocument)expected.Mutations[1];
            var vector = (PutVector)expected.Mutations[2];
            await Assert.That(first.Access!.OwnerId).IsEqualTo("owner");
            await Assert.That(first.ExpectedRevision == 41 && first.ExplicitReplacement).IsTrue();
            await Assert.That(second.Access is null && second.ExpectedRevision is null).IsTrue();
            await Assert.That(vector.Values.Length * sizeof(float)).IsEqualTo(size);
            await Assert.That(vector.Space.Dimension).IsEqualTo(vector.Values.Length);
            await Assert.That(vector.Values[0]).IsEqualTo(-15f / 16);
        }
        finally
        {
            fixture.Cleanup();
        }
        await Assert.That(fixture.JsonEncode).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(1024)]
    [Arguments(16384)]
    public async Task AcIsPerf001StorageMethodsPreserveRawBytesEmptyAndTombstone(int size)
    {
        var expected = SerializationBenchmarkCorpus.Storage(size);
        var fixture = new StorageSerializationBenchmarks { PayloadBytes = size };
        try
        {
            fixture.Setup();
            await AssertSame(expected, fixture.RuntimeDecode());
            await AssertSame(expected, fixture.JsonDecode());
            await AssertSame(expected, OrleansSerialization.Deserialize<KeyLoad.Storage.StorageMutation[]>(fixture.RuntimeEncode()));
            await AssertSame(expected, JsonDefaults.Deserialize<KeyLoad.Storage.StorageMutation[]>(fixture.JsonEncode()));
            await Assert.That(expected[0].Value!.Value.Length).IsEqualTo(size);
            await Assert.That(expected[1].Value!.Value.IsEmpty).IsTrue();
            await Assert.That(expected[2].Value is null).IsTrue();
            await Assert.That(Encoding.UTF8.GetString(expected[3].Value!.Value.Span)).IsEqualTo("Київ🌍");
        }
        finally
        {
            fixture.Cleanup();
        }
        await Assert.That(fixture.RuntimeEncode).Throws<InvalidOperationException>();
    }

    private static async Task AssertSame<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
}
