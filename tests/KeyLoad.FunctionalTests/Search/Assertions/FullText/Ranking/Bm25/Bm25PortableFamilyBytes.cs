using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.Search;

internal static class Bm25PortableFamilyBytes
{
    internal static string[] Complete(IAtomicStore store, TimeProvider clock, CancellationToken token)
    {
        var work = new ReadExecutionBudget(clock, token);
        return store.Read(view => work.CreateView(view).ScanAll([]).Select(row => Convert.ToHexString(row.Key.Span) + ":"
            + Convert.ToHexString(row.Value.Span)).ToArray());
    }

    internal static async Task RequireHeadsAsync(IAtomicStore store, Bm25CorpusCurrent[] originals, byte[][] originalBytes,
        TimeProvider clock, CancellationToken token)
    {
        var work = new ReadExecutionBudget(clock, token);
        await Assert.That(originalBytes.Length).IsEqualTo(originals.Length);
        for (var index = 0; index < originals.Length; index++)
        {
            var original = originals[index];
            var key = KeySpace.Partition("bm25-corpus-current", original.Partition,
                Bm25PortableFamilyBackupSeed.Collection, Bm25PortableFamilyBackupSeed.Field,
                original.TokenizerVersion, original.PrincipalId);
            var expected = originalBytes[index];
            var actual = store.Read(view => work.Read(view, key));
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.AsSpan().SequenceEqual(expected)).IsTrue();
            Bm25CorpusShape.RequireRecord(key, actual, original.Partition);
        }
    }

    internal static async Task SameAsync(string[] expected, string[] actual)
        => await Assert.That(actual).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
}
