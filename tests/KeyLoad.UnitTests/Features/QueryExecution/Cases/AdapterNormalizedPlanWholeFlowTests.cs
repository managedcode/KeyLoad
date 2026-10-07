using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class AdapterNormalizedPlanWholeFlowTests
{
    private const string AccessPathProperty = "accessPath";
    private const string AtomicPartitionProperty = "atomicPartition";
    private const string ScanBudgetProperty = "scanBudget";
    private const string IdProperty = "id";
    private const string NumberProperty = "number";
    private const string StatusProperty = "status";

    [Test]
    public async Task AcKl051Plan001TypedSqlJsonAndCSharpReturnSameCompletePlanRowsAndDeniedErrors()
    {
        using var database = new TestDatabase();
        AdapterPlanWholeFlowFixture.Seed(database);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("denied", "tenant", [], []))).Get<PrincipalRecord>();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var position = database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var plans = AdapterPlanWholeFlowFixture.Execute(database, engine, "root", explain: true);
        foreach (var plan in plans)
        {
            await Assert.That(JsonDefaults.Serialize(plan).AsSpan().SequenceEqual(JsonDefaults.Serialize(plans[0]))).IsTrue();
            await Assert.That(plan.Rows).HasSingleItem();
            await Assert.That(plan.Rows.Single().EntityId).IsEqualTo("explain");
            await Assert.That(plan.Rows.Single().Revision).IsEqualTo(0L);
            await Assert.That(plan.Cursor).IsNull();
            await Assert.That(plan.CutPosition).IsEqualTo(position);
            using var json = JsonDocument.Parse(plan.Rows.Single().Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
            await Assert.That(json.RootElement.GetProperty(AccessPathProperty).GetString()).IsEqualTo("index:status");
            await Assert.That(json.RootElement.GetProperty(AtomicPartitionProperty).GetString()).IsEqualTo(database.Partition.AtomicPartitionId);
            await Assert.That(json.RootElement.GetProperty(ScanBudgetProperty).GetInt32()).IsEqualTo(database.Database.Limits.MaxScanRecords);
        }
        var sql = new QueryRequest(database.Partition, AdapterPlanWholeFlowFixture.Sql, AdapterPlanWholeFlowFixture.Parameters());
        var failures = new[] { Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("denied", sql)),
            Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("denied", AdapterPlanWholeFlowFixture.Ast(database, false))),
            Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("denied", AdapterPlanWholeFlowFixture.CSharp(database, false))) };
        foreach (var failure in failures)
        {
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(failure.Message).IsEqualTo("The principal cannot perform this operation in this scope.");
        }
        var pages = AdapterPlanWholeFlowFixture.Execute(database, engine, "root", explain: false);
        foreach (var page in pages)
        {
            await Assert.That(page.Rows).HasSingleItem();
            await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(JsonDefaults.Serialize(pages[0]))).IsTrue();
            var row = page.Rows.Single();
            await Assert.That(row.EntityId).IsEqualTo("a");
            await Assert.That(row.Revision).IsEqualTo(1L);
            await Assert.That(row.Redacted).IsFalse();
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
            await Assert.That(json.RootElement.GetProperty(IdProperty).GetString()).IsEqualTo("a");
            await Assert.That(json.RootElement.GetProperty(NumberProperty).GetDecimal()).IsEqualTo(2m);
            await Assert.That(json.RootElement.GetProperty(StatusProperty).GetString()).IsEqualTo("open");
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.AccessPath).IsEqualTo("index:status");
            await Assert.That(page.CutPosition).IsEqualTo(position);
        }
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
