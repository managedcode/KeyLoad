using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class PostgresVectorPayloadContractTests
{
    private const string IdField = "id";
    private const string NumberField = "number";
    private const string PaddingField = "padding";

    [Test]
    public async Task JsonbFieldReconstructionPreservesActualContentAndExcludesServerWhitespace()
    {
        var corpus = new VectorComparisonCorpus(VectorComparisonProfile.Parse("vector-100k-exact-plain-c16"), UnitBenchmarkOptions.Native());
        var document = corpus.Create(42);
        using var fields = JsonDocument.Parse(document.Payload);
        var id = fields.RootElement.GetProperty(IdField).GetString()!;
        var number = fields.RootElement.GetProperty(NumberField).GetInt32();
        var padding = fields.RootElement.GetProperty(PaddingField).GetString()!;
        var canonical = PostgresNativeVectorStorage.CanonicalPayload(id, number, padding);
        await Assert.That(Hash(canonical)).IsEqualTo(Hash(document.Payload));
        var mutated = PostgresNativeVectorStorage.CanonicalPayload(id, number, 'z' + padding[1..]);
        await Assert.That(Hash(mutated) == Hash(document.Payload)).IsFalse();
        await Assert.That(Encoding.UTF8.GetByteCount(mutated)).IsEqualTo(1024);
    }

    [Test]
    public async Task InconsistentReadbackShapeOrLengthIsRejected()
    {
        await Assert.That(() => PostgresNativeVectorStorage.CanonicalPayload("bad-id", 42, "x"))
            .Throws<InvalidDataException>();
        await Assert.That(() => PostgresNativeVectorStorage.CanonicalPayload("v000000042", 42, "x"))
            .Throws<InvalidDataException>();
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
