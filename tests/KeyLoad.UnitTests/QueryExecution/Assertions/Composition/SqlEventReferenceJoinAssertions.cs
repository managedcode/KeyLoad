using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlEventReferenceJoinAssertions
{
    private const string EventIdField = "event_id";
    private const string ScoreField = "score";
    private const string CustomerNameField = "customer_name";

    internal static async Task PageAsync(QueryPage page, long position, bool restricted = false, bool masked = false, bool repaired = false)
    {
        await Assert.That(page.CutPosition).IsEqualTo(position);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-event-entity-reference-inner-join");
        var expected = new List<(string Event, long Revision, string Target, string Name)> { ("z", 1, "c1", "Ada") };
        if (repaired)
        {
            expected.Add(("a", 2, "m", "Max"));
        }
        if (!restricted)
        {
            expected.Add(("m", 3, "c2", "Lin"));
        }
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var row = page.Rows[index];
            var item = expected[index];
            await Assert.That(row.EntityId).IsEqualTo(item.Event);
            await Assert.That(row.Revision).IsEqualTo(item.Revision);
            await Assert.That(row.Sources!.Value).IsEquivalentTo(
                new QueryRowSource[] { new("e", item.Event, item.Revision), new("d", item.Target, 1) }, CollectionOrdering.Matching);
            await Assert.That(row.Redacted).IsEqualTo(masked);
            await Assert.That(row.RedactedFields!.Value).IsEquivalentTo(masked ? new[] { "d.name" } : [], CollectionOrdering.Matching);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(3);
            await Assert.That(json.RootElement.GetProperty(EventIdField).GetString()).IsEqualTo(item.Event);
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
            .Execute("root", SqlEventReferenceJoinFixture.Request(database.Partition)), position, repaired: repaired);
    }
}
