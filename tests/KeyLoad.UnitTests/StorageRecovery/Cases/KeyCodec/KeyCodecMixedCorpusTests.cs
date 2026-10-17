using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class KeyCodecMixedCorpusTests
{
    [Test]
    public async Task TenThousandMixedCompositeKeysRoundTripToNormalizedValues()
    {
        var corpus = KeyCodecMixedCorpus.Create();
        await Assert.That(corpus.Length).IsEqualTo(KeyCodecMixedCorpus.Count);

        foreach (var entry in corpus)
        {
            var decoded = KeyCodec.Decode(KeyCodec.Encode(entry.Components));
            await Assert.That(decoded.Length).IsEqualTo(entry.Components.Length);
            for (var index = 0; index < decoded.Length; index++)
            {
                await KeyCodecNormalizedAssertions.AssertEquivalentAsync(
                    KeyCodecMixedCorpus.Normalize(entry.Components[index]), decoded[index]);
            }
        }
    }

    [Test]
    public async Task MixedCompositeSortMatchesIndependentSemanticOrder()
    {
        var corpus = KeyCodecMixedCorpus.Create();
        var expected = corpus.OrderBy(entry => entry, Comparer<KeyCodecCorpusEntry>.Create(KeyCodecMixedCorpus.Compare))
            .Select(entry => entry.Index).ToArray();
        var actual = corpus.OrderBy(entry => KeyCodec.Encode(entry.Components), BinaryKeyComparer.Instance)
            .Select(entry => entry.Index).ToArray();

        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
    }
}
