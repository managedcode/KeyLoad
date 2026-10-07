using KeyLoad.Client;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class VectorAttachmentScalarStateWholeFlowTests
{
    private const string FirstId = "a";
    private const string UnsupportedDetail = "This expression is outside the supported Q1 C# subset.";

    internal enum ScalarState { Filter, Projection, Explain }

    [Test]
    [Arguments(ScalarState.Filter)]
    [Arguments(ScalarState.Projection)]
    [Arguments(ScalarState.Explain)]
    public async Task ScalarStateCannotBeDiscardedAndNativeHealthyAttachmentRetainsLiteralRanks(ScalarState state)
    {
        using var db = new TestDatabase();
        AdapterVectorWholeFlow.Seed(db);
        var canonical = AdapterVectorWholeFlow.Typed(db);
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var position = db.Store.Position;
        var before = db.Store.GetReadDiagnostics();
        var query = KeyLoadQuery.From<VectorAttachmentDocument>(db.Partition,
            AdapterVectorWholeFlow.Collection, UnitClientOptions.Translation());
        // Construct the supported scalar state before observing attachment rejection.
        query = state switch
        {
            ScalarState.Filter => query.Where(row => QueryFunctions.DocumentId(row) == FirstId),
            ScalarState.Projection => query.Select(row => new { Id = QueryFunctions.DocumentId(row) }),
            ScalarState.Explain => query.Explain(),
            _ => throw new InvalidOperationException("The scalar-state case is not defined.")
        };
        GraphSearchRequest? lowered = null;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => lowered = query.AttachVector(
            row => row.Embedding, canonical.Search.Vector!.Value,
            AdapterVectorWholeFlow.Space(), canonical.Scope!));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(error.Message).IsEqualTo(UnsupportedDetail);
        await Assert.That(lowered).IsNull();
        await Assert.That(db.Store.GetReadDiagnostics()).IsEqualTo(before);
        await UnchangedAsync(db, image, position);
        await AdapterVectorWholeFlow.HealthyAsync(db,
            new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution()),
            new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()),
            TestContext.Current!.Execution.CancellationToken);
        await UnchangedAsync(db, image, position);
    }

    private static async Task UnchangedAsync(TestDatabase db, string[] image, long position)
    {
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(db.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
