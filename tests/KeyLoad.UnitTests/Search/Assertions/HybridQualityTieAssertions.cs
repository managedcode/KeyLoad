using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class HybridQualityTieAssertions
{
    internal static async Task VerifyAsync(TestDatabase database, ImmutableArray<RankedDocument> hits)
    {
        var pairs = database.Database.WithVectors(HybridQualityCorpus.PrincipalId,
            database.Partition, HybridQualityCorpus.Collection, HybridQualityCorpus.VectorField,
            (_, _, records) => records.Where(pair => pair.Document.Reference.Id is "d20" or "d21").ToArray());
        await Assert.That(pairs.Length).IsEqualTo(2);
        foreach (var pair in pairs)
        {
            await Assert.That(pair.Vector.Space).IsEqualTo(HybridQualityCorpus.Space);
            await Assert.That(pair.Vector.DocumentRevision).IsEqualTo(1);
            await Assert.That(pair.Vector.Values.AsSpan().SequenceEqual([0.5f, 0.5f, 0.5f, 0.5f])).IsTrue();
        }
        await Assert.That(hits.Length).IsEqualTo(HybridQualityCorpus.ResultLimit);
        await Assert.That(hits[0].Document.Reference.Id).IsEqualTo("d20");
        await Assert.That(hits[1].Document.Reference.Id).IsEqualTo("d21");
        await Assert.That(hits[0].Score).IsEqualTo(1d / 61);
        await Assert.That(hits[1].Score).IsEqualTo(1d / 62);
    }
}
