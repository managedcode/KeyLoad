using KeyLoad.Query;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryResultByteLimitTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ExplicitResultCeilingRejectsCompleteScalarOrJoinPageAndHealthyProjectionPreservesSources(bool ast, bool join)
    {
        using var database = new TestDatabase();
        QueryResultByteLimitFixture.Seed(database, 2_200);
        var original = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var baseline = QueryResultByteLimitFixture.Execute(original, database, ast, join, false);
        await Assert.That(baseline.Rows.Length).IsEqualTo(2);
        await Assert.That(JsonDefaults.Serialize(baseline).Length).IsGreaterThan(QueryResultByteLimitFixture.ResultCap);
        var sources = QueryResultByteLimitFixture.Sources(database);
        var position = database.Store.Position;
        var bounded = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution(
            new QueryExecutionOptions { MaximumResultBytes = QueryResultByteLimitFixture.ResultCap }));
        QueryPage? partial = null;
        KeyLoadException? failure = null;
        try
        {
            partial = QueryResultByteLimitFixture.Execute(bounded, database, ast, join, false);
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure!.Message).IsEqualTo(join
            ? "The read result byte budget is exceeded." : "The query result byte budget is exceeded.");
        await Assert.That(partial).IsNull();
        await VerifyHealthyAsync(database, bounded, ast, join, sources, position);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NullOrLargerResultCeilingStillRejectsAboveNativeBatchLimitAndHealthyReadWorks(bool larger)
    {
        using var database = new TestDatabase(new DatabaseLimits { MaxBatchBytes = QueryResultByteLimitFixture.NativeCap });
        QueryResultByteLimitFixture.Seed(database, 5_000);
        var sources = QueryResultByteLimitFixture.Sources(database);
        var position = database.Store.Position;
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution(new QueryExecutionOptions
        { MaximumResultBytes = larger ? QueryResultByteLimitFixture.LargerCap : null }));
        KeyLoadException? failure = null;
        QueryPage? partial = null;
        try
        {
            partial = QueryResultByteLimitFixture.Execute(engine, database, false, false, false);
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure!.Message).IsEqualTo("The query result byte budget is exceeded.");
        await Assert.That(partial).IsNull();
        await VerifyHealthyAsync(database, engine, false, false, sources, position);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task InvalidResultCeilingDeniesOwnerAdmissionBeforeNativeReadAndHealthyOwnerStillWorks(int maximum)
    {
        using var database = new TestDatabase();
        QueryResultByteLimitFixture.Seed(database, 32);
        var sources = QueryResultByteLimitFixture.Sources(database);
        var position = database.Store.Position;
        var native = database.Store.GetReadDiagnostics();
        InvalidOperationException? failure = null;
        try
        {
            var rejected = new QueryEngine(database.Database, Options.Create(new QueryExecutionOptions
            { MaximumResultBytes = maximum }));
            QueryResultByteLimitFixture.Execute(rejected, database, false, false, true);
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).IsEqualTo(QueryExecutionOptions.ValidationMessage);
        await Assert.That(database.Store.GetReadDiagnostics().RangeExaminedBytes).IsEqualTo(native.RangeExaminedBytes);
        var healthy = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        await VerifyHealthyAsync(database, healthy, false, false, sources, position);
    }

    private static async Task VerifyHealthyAsync(TestDatabase database, QueryEngine engine,
        bool ast, bool join, byte[] sources, long position)
    {
        await Assert.That(QueryResultByteLimitFixture.Sources(database)).IsEquivalentTo(sources, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthy = QueryResultByteLimitFixture.Execute(engine, database, ast, join, true);
        await Assert.That(healthy.Rows.Select(row => row.EntityId)).IsEquivalentTo(
            new[] { "order-a", "order-b" }, CollectionOrdering.Matching);
        await Assert.That(healthy.Rows.Select(row => row.Json)).IsEquivalentTo(
            new[] { "{\"key\":\"order-a\"}", "{\"key\":\"order-b\"}" }, CollectionOrdering.Matching);
        await Assert.That(healthy.Rows.All(row => row.Revision == 1 && !row.Redacted
            && row.RedactedFields is { IsEmpty: true })).IsTrue();
        if (join)
        {
            for (var index = 0; index < healthy.Rows.Length; index++)
            {
                var suffix = index == 0 ? "a" : "b";
                await Assert.That(healthy.Rows[index].Sources!.Value).IsEquivalentTo(
                    new[] { new QueryRowSource("l", "order-" + suffix, 1),
                        new QueryRowSource("r", "customer-" + suffix, 1) }, CollectionOrdering.Matching);
            }
        }
        await Assert.That(healthy.CutPosition).IsEqualTo(position);
        await Assert.That(healthy.Cursor).IsNull();
        await Assert.That(QueryResultByteLimitFixture.Sources(database)).IsEquivalentTo(sources, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }
}
