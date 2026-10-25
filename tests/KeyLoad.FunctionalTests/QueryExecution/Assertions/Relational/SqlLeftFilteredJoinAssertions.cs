using System.Text.Json;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlLeftFilteredJoinAssertions
{
    private const string OrderIdField = "order_id";
    private const string TotalField = "total";
    private const string CustomerNameField = "customer_name";

    internal static async Task PageAsync(QueryPage page, long position, bool masked = false)
    {
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-primary-key-inner-join");
        await Assert.That(page.CutPosition).IsEqualTo(position);
        await Assert.That(page.Rows).HasSingleItem();
        var row = page.Rows.Single();
        await Assert.That(row.EntityId).IsEqualTo("a");
        await Assert.That(row.Revision).IsEqualTo(1L);
        await Assert.That(row.Sources!.Value).IsEquivalentTo(
            new QueryRowSource[] { new("l", "a", 1), new("r", "c1", 1) }, CollectionOrdering.Matching);
        await Assert.That(row.Redacted).IsEqualTo(masked);
        await Assert.That(row.RedactedFields!.Value).IsEquivalentTo(
            masked ? new[] { "r.name" } : [], CollectionOrdering.Matching);
        using var json = JsonDocument.Parse(row.Json);
        await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
        await Assert.That(json.RootElement.GetProperty(OrderIdField).GetString()).IsEqualTo("a");
        await Assert.That(json.RootElement.GetProperty(TotalField).GetInt32()).IsEqualTo(7);
        if (masked)
        {
            await Assert.That(json.RootElement.GetProperty(CustomerNameField).ValueKind).IsEqualTo(JsonValueKind.Null);
        }
        else
        {
            await Assert.That(json.RootElement.GetProperty(CustomerNameField).GetString()).IsEqualTo("Ada");
        }
    }

    internal static async Task RejectAsync(TestDatabase database, Action operation, ErrorCode code)
    {
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await PageAsync(new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution())
            .Execute("root", SqlLeftFilteredJoinFixture.Request(database.Partition)), position);
    }
}
