using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>KL027: canonical vector failure and successful replacement share the document revision boundary.</summary>
internal sealed class CanonicalVectorAtomicRevisionTests
{
    private const string Collection = "canonical-vectors";
    private const string Root = "root";
    private const string Target = "target";
    private const string Rival = "rival";
    private const string Field = "/embedding";
    private const string OriginalJson = "{\"state\":\"original\"}";
    private const string UpdatedJson = "{\"state\":\"updated\"}";
    private static readonly VectorSpace Space = new("canonical", 2, DistanceMetric.DotProduct, "model", "1");

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Kl027LateVectorFailureRollsBackDocumentAndHealthyUpdatePublishesRevisionTogether(bool dimensionFailure)
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, Target, OriginalJson), new PutDocument(Collection, Rival, "{}"),
            new PutVector(Collection, Target, Field, [1, 0], Space, 1),
            new PutVector(Collection, Rival, Field, [0.5f, 0.5f], Space, 1));
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(database.Partition, Collection, VectorField: Field, Vector: [1, 0], Space: Space);
        var before = await search.SearchAsync(Root, request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(before.Select(row => row.Document.Reference.Id)).IsEquivalentTo(
            new[] { Target, Rival }, CollectionOrdering.Matching);
        var original = database.Database.GetDocument(Root, new(database.Partition, Collection, Target))!;
        await Assert.That(original.Reference).IsEqualTo(new EntityRef(database.Partition, Collection, Target));
        await Assert.That(original.Json).IsEqualTo(OriginalJson);
        await Assert.That(original.Revision).IsEqualTo(1L);
        var position = database.Store.Position;
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, database.Partition,
            [new PutDocument(Collection, Target, UpdatedJson, 1),
                new PutVector(Collection, Target, Field, dimensionFailure ? [1] : [0, 1], Space,
                    dimensionFailure ? 2 : 1)]);
        await CanonicalVectorRejectedOutcomeAssertions.VerifyReplayAsync(database, command, dimensionFailure, position);
        var after = database.Database.GetDocument(Root, new(database.Partition, Collection, Target))!;
        await Assert.That(JsonDefaults.Serialize(after)).IsEquivalentTo(JsonDefaults.Serialize(original), CollectionOrdering.Matching);
        var unchanged = await search.SearchAsync(Root, request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(JsonDefaults.Serialize(unchanged)).IsEquivalentTo(JsonDefaults.Serialize(before), CollectionOrdering.Matching);
        database.Commit(new PutDocument(Collection, Target, UpdatedJson, 1),
            new PutVector(Collection, Target, Field, [0, 1], Space, 2));
        var healthy = await search.SearchAsync(Root, request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(healthy.Select(row => row.Document.Reference.Id)).IsEquivalentTo(
            new[] { Rival, Target }, CollectionOrdering.Matching);
        var updated = healthy.Single(row => row.Document.Reference.Id == Target).Document;
        await Assert.That(updated.Reference).IsEqualTo(new EntityRef(database.Partition, Collection, Target));
        await Assert.That(updated.Json).IsEqualTo(UpdatedJson);
        await Assert.That(updated.Revision).IsEqualTo(2L);
        await Assert.That(await search.SearchAsync(Root, request with { Space = Space with { Model = "other-model" } },
            TestContext.Current!.Execution.CancellationToken)).IsEmpty();
        var recovered = await search.SearchAsync(Root, request with { Vector = [0, 1] },
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(recovered.Select(row => row.Document.Reference.Id)).IsEquivalentTo(
            new[] { Target, Rival }, CollectionOrdering.Matching);
    }
}
