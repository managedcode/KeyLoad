using System.Text.Json;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlQueueReferenceJoinAssertions
{
    private const string MessageIdField = "message_id";
    private const string CustomerNameField = "customer_name";
    private const string ScoreField = "score";

    internal static async Task PageAsync(QueryPage page, long position, bool restricted = false, bool masked = false, bool repaired = false)
    {
        await Assert.That(page.CutPosition).IsEqualTo(position);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-queue-entity-reference-inner-join");
        var expected = new List<(string Message, string Target, string Name)> { ("a", "c1", "Ada") };
        if (repaired)
        {
            expected.Add(("b", "m", "Max"));
        }
        if (!restricted)
        {
            expected.Add(("c", "c2", "Lin"));
        }
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var row = page.Rows[index];
            var item = expected[index];
            await Assert.That(row.EntityId).IsEqualTo(item.Message);
            await Assert.That(row.Revision).IsEqualTo(1L);
            await Assert.That(row.Sources!.Value).IsEquivalentTo(
                new QueryRowSource[] { new("q", item.Message, 1), new("d", item.Target, 1) }, CollectionOrdering.Matching);
            await Assert.That(row.Redacted).IsEqualTo(masked);
            await Assert.That(row.RedactedFields!.Value).IsEquivalentTo(masked ? new[] { "d.name" } : [], CollectionOrdering.Matching);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
            await Assert.That(json.RootElement.GetProperty(MessageIdField).GetString()).IsEqualTo(item.Message);
            await Assert.That(json.RootElement.GetProperty(CustomerNameField).GetString()).IsEqualTo(masked ? null : item.Name);
            await Assert.That(json.RootElement.GetProperty(ScoreField).GetInt32()).IsEqualTo(7);
        }
    }

    internal static async Task RejectAsync(TestDatabase database, Action operation, ErrorCode code, bool repaired = false)
    {
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await PageAsync(new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution())
            .Execute("root", SqlQueueReferenceJoinFixture.Request(database.Partition)), position, repaired: repaired);
    }
}
