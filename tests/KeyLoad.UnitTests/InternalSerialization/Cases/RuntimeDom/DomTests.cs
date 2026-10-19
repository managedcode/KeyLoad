using System.Text.Json;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class DomTests
{
    private const int Repetitions = 9;
    private const int StringDivisor = 8;

    [Test]
    public async Task AcIs002StructuralDomRequiresATreeAndRejectsSharedNodeExpansion()
    {
        var shared = DomFixtures.Null();
        var root = new JsonTreeNode { Kind = JsonValueKind.Array, Items = [shared, shared] };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => DomFixtures.Convert(root)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var actual = DomFixtures.Convert(new()
        {
            Kind = JsonValueKind.Array,
            Items = [DomFixtures.Null(), DomFixtures.Null()]
        });
        await Assert.That(actual.GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task AcIs002StructuralDomCyclesFailBeforeOutputConstruction()
    {
        var children = new JsonTreeNode[1];
        var root = new JsonTreeNode { Kind = JsonValueKind.Array, Items = children };
        children[0] = root;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => DomFixtures.Convert(root)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002DistinctNodesCannotExpandRepeatedTextPastTheDomByteFence()
    {
        var shared = new string('x', SerializationLimits.MaximumDomBytes / StringDivisor);
        var items = Enumerable.Range(0, Repetitions).Select(_ => DomFixtures.Text(shared)).ToArray();
        var root = new JsonTreeNode { Kind = JsonValueKind.Array, Items = items };
        _ = DomFixtures.Convert(DomFixtures.Text(DomFixtures.Name));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => DomFixtures.Convert(root));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(allocated < SerializationLimits.MaximumDomBytes / 2).IsTrue();
    }

    [Test]
    public async Task AcIs002DistinctPropertiesCannotExpandRepeatedNamesPastTheDomByteFence()
    {
        var shared = new string('x', SerializationLimits.MaximumDomBytes / StringDivisor);
        var properties = Enumerable.Range(0, Repetitions).Select(_ => new JsonTreeProperty
        {
            Name = shared,
            Value = DomFixtures.Null()
        }).ToArray();
        var root = new JsonTreeNode { Kind = JsonValueKind.Object, Properties = properties };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => DomFixtures.Convert(root)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002EscapedUtf8OutputRatherThanSourceCharactersDefinesTheDomFence()
    {
        const int EscapedWidth = 6;
        var text = new string('<', SerializationLimits.MaximumDomBytes / EscapedWidth + 1);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => DomFixtures.Convert(DomFixtures.Text(text))).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002DomDepthFencePreservesTheJsonEdgeAndRejectsTheNextContainer()
    {
        var actual = DomFixtures.Convert(DomFixtures.Arrays(SerializationLimits.SemanticDepth));
        await Assert.That(actual.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => DomFixtures.Convert(
            DomFixtures.Arrays(SerializationLimits.SemanticDepth + 1))).Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002EscapedStringsAndOrderedDuplicatePropertiesRetainTheirValues()
    {
        var root = new JsonTreeNode
        {
            Kind = JsonValueKind.Object,
            Properties =
        [
            new() { Name = DomFixtures.Name, Value = DomFixtures.Text(DomFixtures.EscapedText) },
            new() { Name = DomFixtures.Name, Value = new() { Kind = JsonValueKind.Number, Text = DomFixtures.Number } }
        ]
        };
        var actual = DomFixtures.Convert(root);
        var values = actual.EnumerateObject().ToArray();
        await Assert.That(values.Length).IsEqualTo(2);
        await Assert.That(values[0].Value.GetString()).IsEqualTo(DomFixtures.EscapedText);
        await Assert.That(values[1].Value.GetRawText()).IsEqualTo(DomFixtures.Number);
    }

    [Test]
    public async Task AcIs002StringSizingKeepsASurrogatePairAtTheChunkBoundaryIntact()
    {
        const int ChunkBoundary = 4_095;
        const string SupplementaryScalar = "🙂";
        var text = new string('x', ChunkBoundary) + SupplementaryScalar + DomFixtures.EscapedText;
        var actual = DomFixtures.Convert(DomFixtures.Text(text));
        await Assert.That(actual.GetString()).IsEqualTo(text);
    }
}
