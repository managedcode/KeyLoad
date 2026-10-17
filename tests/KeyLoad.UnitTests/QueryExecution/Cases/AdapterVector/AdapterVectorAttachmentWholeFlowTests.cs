using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class AdapterVectorAttachmentWholeFlowTests
{
    [Test]
    public async Task AcKl051Vector001ActualNamedAttachmentAndTypedJsonRejectWrongDimensionThenReturnCompleteLiteralRanks()
    {
        using var database = new TestDatabase();
        AdapterVectorWholeFlow.Seed(database);
        var query = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var token = TestContext.Current!.Execution.CancellationToken;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var originalNativeWork = database.Store.GetReadDiagnostics();
        GraphSearchResult? rejectedResult = null;
        var sqlFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            rejectedResult = await query.SearchSqlAsync("root", AdapterVectorWholeFlow.SqlRequest(database, invalid: true), token)))
            ?? throw new InvalidOperationException("The rejected SQL operation must return its exact failure.");
        await Assert.That(sqlFailure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(sqlFailure.Message).IsEqualTo("A valid vector space and matching finite vector parameter are required.");
        foreach (var input in new[] { AdapterVectorWholeFlow.Typed(database, invalid: true),
            AdapterVectorWholeFlow.JsonRoundTrip(AdapterVectorWholeFlow.Typed(database, invalid: true)),
            AdapterVectorWholeFlow.CSharp(database, invalid: true) })
        {
            var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                rejectedResult = await search.GraphSearchAsync("root", input, token)))
                ?? throw new InvalidOperationException("The rejected typed operation must return its exact failure.");
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(failure.Message).IsEqualTo("A finite vector and a matching typed vector space are required.");
        }
        await Assert.That(rejectedResult).IsNull();
        await Assert.That(database.Store.GetReadDiagnostics()).IsEqualTo(originalNativeWork);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await AdapterVectorWholeFlow.HealthyAsync(database, query, search, token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
