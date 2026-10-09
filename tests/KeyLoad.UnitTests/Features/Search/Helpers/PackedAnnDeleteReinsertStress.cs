using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnDeleteReinsertStress
{
    internal const int RecordCount = 64;
    internal const int Dimension = 8;
    private const int Cycles = 16;
    private const int ChangedOrdinal = 1;
    private const int QueryOrdinal = 2;
    private const int GraphThreshold = 0;
    private const int RevisionStep = 1;
    private const int InitialCycle = 0;
    private const int FirstRecord = 0;
    private const string DocumentJson = "{}";
    internal static readonly PackedAnnOptions Policy = new() { ExactThreshold = GraphThreshold };

    internal static async Task RunAsync(TestDatabase database, DistanceMetric metric, CancellationToken token)
    {
        var space = PackedAnnTestData.Space(metric, Dimension);
        var query = PackedAnnTestData.Vector(QueryOrdinal, Dimension, PackedAnnTestData.CorpusSeed);
        var records = PackedAnnTestData.Load(database, metric);
        var index = Build(database, space, records, token);
        await PackedAnnStressOracle.VerifyAsync(index, records, query, database, token);
        for (var cycle = InitialCycle; cycle < Cycles; cycle++)
        {
            token.ThrowIfCancellationRequested();
            await ChangeAsync(database, metric, space, index, records, query, cycle, token);
            records = PackedAnnTestData.Load(database, metric);
            index = Build(database, space, records, token);
            await PackedAnnStressOracle.VerifyAsync(index, records, query, database, token);
            await RejectRebuildAsync(database, space, index, records, query, token);
        }
        await PackedAnnStressColdRestore.VerifyAsync(database, index, records, query, metric, token);
    }

    private static async Task ChangeAsync(TestDatabase database, DistanceMetric metric, VectorSpace space,
        PackedAnnIndex previous, VectorRecord[] records, float[] query, int cycle, CancellationToken token)
    {
        var id = PackedAnnTestData.DocumentId(ChangedOrdinal);
        var revision = records.Single(row => row.DocumentId == id).DocumentRevision;
        var deleted = database.Commit(new DeleteDocument(PackedAnnTestData.Collection, id, revision));
        await Assert.That(deleted.Token.Position).IsEqualTo(database.Store.Position);
        var remaining = PackedAnnTestData.Load(database, metric);
        await Assert.That(remaining.Length).IsEqualTo(RecordCount - RevisionStep);
        await Assert.That(remaining.Select(row => row.DocumentId)).DoesNotContain(id);
        var without = Build(database, space, remaining, token);
        await PackedAnnStressOracle.VerifyAsync(without, remaining, query, database, token);
        await PackedAnnStressOracle.VerifyAsync(previous, records, query, database, token);
        var restoredRevision = revision + RevisionStep + RevisionStep;
        var vector = PackedAnnTestData.Vector(RecordCount + cycle, Dimension, PackedAnnTestData.CorpusSeed);
        var restored = database.Commit(new PutDocument(PackedAnnTestData.Collection, id, DocumentJson, revision + RevisionStep),
            new PutVector(PackedAnnTestData.Collection, id, PackedAnnTestData.Field(metric),
                ImmutableArray.CreateRange(vector), space, restoredRevision));
        await Assert.That(restored.Token.Position).IsEqualTo(database.Store.Position);
        var recreated = PackedAnnTestData.Load(database, metric).Single(row => row.DocumentId == id);
        await Assert.That(recreated.DocumentRevision).IsEqualTo(restoredRevision);
        await Assert.That(recreated.Values.SequenceEqual(vector)).IsTrue();
        await PackedAnnStressOracle.VerifyAsync(previous, records, query, database, token);
    }

    private static async Task RejectRebuildAsync(TestDatabase database, VectorSpace space, PackedAnnIndex index,
        VectorRecord[] records, float[] query, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Build(database, space, [.. records, records[FirstRecord]], token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await PackedAnnStressOracle.UnchangedAsync(database.Store, bytes, position);
        await PackedAnnStressOracle.VerifyAsync(index, records, query, database, token);
    }

    private static PackedAnnIndex Build(TestDatabase database, VectorSpace space, VectorRecord[] records,
        CancellationToken token) => PackedAnnIndexTestSupport.Build(space, records, Policy,
        PackedAnnIndexTestSupport.Budget(database, token: token));
}
