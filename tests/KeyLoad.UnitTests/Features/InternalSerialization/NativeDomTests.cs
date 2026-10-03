using System.Text.Json;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeDomTests
{
    private const int Repetitions = 9;
    private const int StringDivisor = 8;

    [Test]
    public async Task AcIs002StructuralDomRequiresATreeAndRejectsSharedNodeExpansion()
    {
        var shared = NativeDomFixtures.Null();
        var root = new JsonTreeNode { Kind = JsonValueKind.Array, Items = [shared, shared] };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeDomFixtures.Convert(root)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var actual = NativeDomFixtures.Convert(new() { Kind = JsonValueKind.Array, Items = [NativeDomFixtures.Null(), NativeDomFixtures.Null()] });
        await Assert.That(actual.GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task AcIs002StructuralDomCyclesFailBeforeOutputConstruction()
    {
        var children = new JsonTreeNode[1];
        var root = new JsonTreeNode { Kind = JsonValueKind.Array, Items = children };
        children[0] = root;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeDomFixtures.Convert(root)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002DistinctNodesCannotExpandRepeatedTextPastTheDomByteFence()
    {
        var shared = new string('x', NativeSerializationLimits.MaximumDomBytes / StringDivisor);
        var items = Enumerable.Range(0, Repetitions).Select(_ => NativeDomFixtures.Text(shared)).ToArray();
        var root = new JsonTreeNode { Kind = JsonValueKind.Array, Items = items };
        _ = NativeDomFixtures.Convert(NativeDomFixtures.Text(NativeDomFixtures.Name));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeDomFixtures.Convert(root));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(allocated < NativeSerializationLimits.MaximumDomBytes / 2).IsTrue();
    }

    [Test]
    public async Task AcIs002DistinctPropertiesCannotExpandRepeatedNamesPastTheDomByteFence()
    {
        var shared = new string('x', NativeSerializationLimits.MaximumDomBytes / StringDivisor);
        var properties = Enumerable.Range(0, Repetitions).Select(_ => new JsonTreeProperty
        { Name = shared, Value = NativeDomFixtures.Null() }).ToArray();
        var root = new JsonTreeNode { Kind = JsonValueKind.Object, Properties = properties };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeDomFixtures.Convert(root)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002EscapedUtf8OutputRatherThanSourceCharactersDefinesTheDomFence()
    {
        const int EscapedWidth = 6;
        var text = new string('<', NativeSerializationLimits.MaximumDomBytes / EscapedWidth + 1);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeDomFixtures.Convert(NativeDomFixtures.Text(text))).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002DomDepthFencePreservesTheJsonEdgeAndRejectsTheNextContainer()
    {
        var actual = NativeDomFixtures.Convert(NativeDomFixtures.Arrays(NativeSerializationLimits.SemanticDepth));
        await Assert.That(actual.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeDomFixtures.Convert(
            NativeDomFixtures.Arrays(NativeSerializationLimits.SemanticDepth + 1))).Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002EscapedStringsAndOrderedDuplicatePropertiesRetainTheirValues()
    {
        var root = new JsonTreeNode
        {
            Kind = JsonValueKind.Object,
            Properties =
        [
            new() { Name = NativeDomFixtures.Name, Value = NativeDomFixtures.Text(NativeDomFixtures.EscapedText) },
            new() { Name = NativeDomFixtures.Name, Value = new() { Kind = JsonValueKind.Number, Text = NativeDomFixtures.Number } }
        ]
        };
        var actual = NativeDomFixtures.Convert(root);
        var values = actual.EnumerateObject().ToArray();
        await Assert.That(values.Length).IsEqualTo(2);
        await Assert.That(values[0].Value.GetString()).IsEqualTo(NativeDomFixtures.EscapedText);
        await Assert.That(values[1].Value.GetRawText()).IsEqualTo(NativeDomFixtures.Number);
    }

    [Test]
    public async Task AcIs002StringSizingKeepsASurrogatePairAtTheChunkBoundaryIntact()
    {
        const int ChunkBoundary = 4_095;
        const string SupplementaryScalar = "🙂";
        var text = new string('x', ChunkBoundary) + SupplementaryScalar + NativeDomFixtures.EscapedText;
        var actual = NativeDomFixtures.Convert(NativeDomFixtures.Text(text));
        await Assert.That(actual.GetString()).IsEqualTo(text);
    }
}
