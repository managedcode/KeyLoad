using System.Runtime.InteropServices;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnOwnershipTests
{
    [Test]
    public async Task AcAnn006BuildOwnsInputListAndVectorBuffersAndFailedBuildKeepsPriorIndex()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, 2);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric), [[1, 0], [0, 1]]);
        var records = PackedAnnTestData.Load(database, metric);
        var index = PackedAnnIndexTestSupport.Build(space, records, new(), PackedAnnIndexTestSupport.Budget(database));
        var query = new float[] { 1, 0 };
        var original = records[0];
        var before = index.Search(query, 2, null, PackedAnnIndexTestSupport.Budget(database));

        var borrowedComponents = ImmutableCollectionsMarshal.AsArray(original.Values)!;
        borrowedComponents[0] = -100;
        records[0] = records[1];
        await Assert.That(original.Values[0]).IsEqualTo(-100f);
        await Assert.That(records[0].DocumentId).IsEqualTo(records[1].DocumentId);
        await AssertValidation(() => PackedAnnIndex.Build(space, [records[1], original], UnitExecutionOptions.PackedAnn(new()),
            PackedAnnIndexTestSupport.Budget(database)));

        var after = index.Search(query, 2, null, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(after.Candidates.Select(candidate => candidate.DocumentId))
            .IsEquivalentTo(before.Candidates.Select(candidate => candidate.DocumentId));
        for (var rank = 0; rank < before.Candidates.Length; rank++)
        {
            await Assert.That(after.Candidates[rank].Score).IsEqualTo(before.Candidates[rank].Score);
            await Assert.That(after.Candidates[rank].DocumentRevision).IsEqualTo(before.Candidates[rank].DocumentRevision);
        }
        await Assert.That(after.Candidates[0].DocumentId).IsEqualTo(PackedAnnTestData.DocumentId(0));
    }

    private static async Task AssertValidation(Action operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }
}
