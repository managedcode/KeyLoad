using System.Collections.Immutable;
using KeyLoad.Query.Search;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class PackedAnnRebuildTests
{
    [Test]
    public async Task AcAnn006RebuildReflectsVectorUpdateStaleRevisionDeleteAndReinsert()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, 2);
        var field = PackedAnnTestData.Field(metric);
        PackedAnnIndexTestSupport.PersistVectors(database, space, field, [[1, 0], [0, 1], [-1, 0]]);
        var originalRecords = PackedAnnTestData.Load(database, metric);
        var original = PackedAnnIndexTestSupport.Build(space, originalRecords, new(), PackedAnnIndexTestSupport.Budget());
        var query = new float[] { 1, 0 };

        database.Commit(new PutVector(PackedAnnTestData.Collection, originalRecords[0].DocumentId, field,
            ImmutableArray.Create(-1f, 0f), space, PackedAnnTestData.DocumentRevision));
        var vectorUpdatedRecords = PackedAnnTestData.Load(database, metric);
        var vectorUpdated = Build(space, vectorUpdatedRecords);
        await Assert.That(vectorUpdatedRecords[0].DocumentRevision).IsEqualTo(1);
        await Assert.That(TopId(vectorUpdated, query)).IsEqualTo(PackedAnnTestData.DocumentId(1));
        await Assert.That(TopId(original, query)).IsEqualTo(PackedAnnTestData.DocumentId(0));

        database.Commit(new PatchDocument(PackedAnnTestData.Collection, PackedAnnTestData.DocumentId(0),
            [new("/state", PatchKind.Set, "true")], 1));
        var staleRecords = PackedAnnTestData.Load(database, metric);
        await Assert.That(staleRecords.Select(record => record.DocumentId))
            .DoesNotContain(PackedAnnTestData.DocumentId(0));
        await Assert.That(staleRecords.Length).IsEqualTo(2);

        database.Commit(new PutVector(PackedAnnTestData.Collection, PackedAnnTestData.DocumentId(0), field,
            ImmutableArray.Create(1f, 0f), space, 2));
        var refreshedRecords = PackedAnnTestData.Load(database, metric);
        var refreshed = Build(space, refreshedRecords);
        await Assert.That(refreshedRecords.Single(record => record.DocumentId == PackedAnnTestData.DocumentId(0))
            .DocumentRevision).IsEqualTo(2);
        await Assert.That(TopId(refreshed, query)).IsEqualTo(PackedAnnTestData.DocumentId(0));

        database.Commit(new DeleteDocument(PackedAnnTestData.Collection, PackedAnnTestData.DocumentId(1), 1));
        var deletedRecords = PackedAnnTestData.Load(database, metric);
        await Assert.That(deletedRecords.Select(record => record.DocumentId))
            .DoesNotContain(PackedAnnTestData.DocumentId(1));
        database.Commit(new PutDocument(PackedAnnTestData.Collection, PackedAnnTestData.DocumentId(1), "{}", 2));
        database.Commit(new PutVector(PackedAnnTestData.Collection, PackedAnnTestData.DocumentId(1), field,
            ImmutableArray.Create(0.5f, 0.5f), space, 3));
        var reinsertedRecords = PackedAnnTestData.Load(database, metric);
        var reinserted = Build(space, reinsertedRecords);
        var recreated = reinsertedRecords.Single(record => record.DocumentId == PackedAnnTestData.DocumentId(1));
        await Assert.That(recreated.DocumentRevision).IsEqualTo(3);
        await Assert.That(TopId(reinserted, query)).IsEqualTo(PackedAnnTestData.DocumentId(0));
        await Assert.That(TopId(refreshed, query)).IsEqualTo(PackedAnnTestData.DocumentId(0));
    }

    private static PackedAnnIndex Build(VectorSpace space, VectorRecord[] records)
        => PackedAnnIndexTestSupport.Build(space, records, new(), PackedAnnIndexTestSupport.Budget());

    private static string TopId(PackedAnnIndex index, float[] query)
        => index.Search(query, 1, null, PackedAnnIndexTestSupport.Budget()).Candidates.Single().DocumentId;
}
