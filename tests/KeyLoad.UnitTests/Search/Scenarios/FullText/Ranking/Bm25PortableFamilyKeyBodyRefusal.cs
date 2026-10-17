using KeyLoad.Core.Features.Search;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.BackupRestore;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PortableFamilyKeyBodyRefusal
{
    private const string OriginalRequired = "The original native BM25 publication result is required.";

    internal static async Task<(Bm25CorpusCurrentV1 Head, byte[] Bytes)> RunAsync(
        EventingArtifactFixture fixture, Bm25CorpusCurrentV1[] heads, byte[][] bytes, CancellationToken token)
    {
        await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Source.Store, heads, bytes,
            fixture.Source.Database.EvaluationClock, token);
        await Assert.That(heads[0].PrincipalId).IsNotEqualTo(heads[1].PrincipalId);
        var key = Bm25CorpusKeys.Current(heads[0]);
        await Assert.That(key.AsSpan().SequenceEqual(Bm25CorpusKeys.Current(heads[1]))).IsFalse();
        var originalRows = Bm25PortableFamilyBytes.Complete(fixture.Source.Store,
            fixture.Source.Database.EvaluationClock, token);
        var originalEvidence = ReadEvidence(fixture.Source, heads);
        var failures = new List<Exception>();
        var repairRequired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            repairRequired = true;
            fixture.Source.Store.Commit((transaction, _) => { transaction.Put(key, bytes[1]); return true; });
            await RejectAsync(fixture, "bm25-keybody-first", token);
            await RejectAsync(fixture, "bm25-keybody-repeated", token);
            await RequireEvidenceAsync(originalEvidence, ReadEvidence(fixture.Source, heads));
        }, failures);
        if (repairRequired)
        {
            ServerFailureObserver.Observe(() => fixture.Source.Store.Commit((transaction, _) =>
                {
                    transaction.Put(key, bytes[0]);
                    return true;
                }), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Bm25PortableFamilyBytes.SameAsync(originalRows, Bm25PortableFamilyBytes.Complete(
            fixture.Source.Store, fixture.Source.Database.EvaluationClock, token));
        await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Source.Store, heads, bytes,
            fixture.Source.Database.EvaluationClock, token);
        await RequireEvidenceAsync(originalEvidence, ReadEvidence(fixture.Source, heads));
        return await Bm25PortableFamilyBackupSeed.PublishAsync(fixture.Source,
            heads[0].PrincipalId, token, heads[0].Generation);
    }

    private static async Task RejectAsync(EventingArtifactFixture fixture, string output, CancellationToken token)
    {
        var source = fixture.Source;
        var rows = Bm25PortableFamilyBytes.Complete(source.Store, source.Database.EvaluationClock, token);
        var identity = source.Store.Identity;
        var position = source.Store.Position;
        var applied = source.Database.LastApplied;
        var journal = source.ReadJournalCut();
        var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() =>
            MixedEventingCatalogRestore.Capture(source, Path.Combine(fixture.Root, output), token), token));
        await Assert.That(refused!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(refused.Message).IsEqualTo(Bm25CorpusProtocol.Invalid);
        await Bm25PortableFamilyBytes.SameAsync(rows, Bm25PortableFamilyBytes.Complete(
            source.Store, source.Database.EvaluationClock, token));
        await Assert.That(source.Store.Identity).IsEqualTo(identity);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(source.ReadJournalCut()).IsEqualTo(journal);
    }

    private static byte[][] ReadEvidence(TestDatabase source, Bm25CorpusCurrentV1[] heads)
    {
        var result = new List<byte[]>();
        foreach (var head in heads)
        {
            result.Add(NativeSerialization.Serialize(source.ReadRetainedEntry(head.Generation)));
            var outcome = OutcomeStoreOracle.ReadPartition(source.Store, source.Partition,
                Bm25PortableFamilyBackupSeed.Root, head.Generation)
                ?? throw new InvalidOperationException(OriginalRequired);
            result.Add(NativeSerialization.Serialize(outcome));
        }
        return result.ToArray();
    }

    private static async Task RequireEvidenceAsync(byte[][] expected, byte[][] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].AsSpan().SequenceEqual(expected[index])).IsTrue();
        }
    }
}
