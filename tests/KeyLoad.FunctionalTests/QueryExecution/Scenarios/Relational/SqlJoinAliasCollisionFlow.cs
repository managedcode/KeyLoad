using System.Text.Json;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlJoinAliasCollisionFlow
{
    private const string LeftAlias = "e";
    private const string MessageIdField = "message_id";
    private const string CustomerNameField = "customer_name";
    private const string ScoreField = "score";
    private const int ExpectedFields = 3;
    private const int ExpectedScore = 7;

    internal static async Task RunAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition, bool repaired)
    {
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var original = SqlQueueReferenceJoinFixture.Request(partition);
        var leftSql = original.Sql.Replace("q.", "e.", StringComparison.Ordinal)
            .Replace(" AS q ", " AS e ", StringComparison.Ordinal);
        var before = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        foreach (var rightAlias in new[] { "payload", "headers" })
        {
            var invalid = leftSql.Replace("d.name AS", rightAlias + ".name AS", StringComparison.Ordinal)
                .Replace(" AS d ON", " AS " + rightAlias + " ON", StringComparison.Ordinal)
                .Replace("d.entity_ref", rightAlias + ".entity_ref", StringComparison.Ordinal)
                .Replace("e.payload.score >= @minimum", rightAlias + ".name = 'Ada'", StringComparison.Ordinal);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root", original with { Sql = invalid }));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await PageAsync(engine.Execute("root", original with
            {
                Sql = leftSql
            }), position, repaired);
            await PageAsync(engine.Execute("root", original with
            {
                Sql = leftSql.Replace("e.payload.score >=", "payload.score >=", StringComparison.Ordinal)
            }), position, repaired);
        }
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task PageAsync(QueryPage page, long position, bool repaired)
    {
        await Assert.That(page.CutPosition).IsEqualTo(position);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-queue-entity-reference-inner-join");
        var expected = new List<(string Message, string Target, string Name)> { ("a", "c1", "Ada") };
        if (repaired)
        {
            expected.Add(("b", "m", "Max"));
        }
        expected.Add(("c", "c2", "Lin"));
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var row = page.Rows[index];
            var item = expected[index];
            await Assert.That(row.EntityId).IsEqualTo(item.Message);
            await Assert.That(row.Revision).IsEqualTo(1L);
            await Assert.That(row.Sources!.Value).IsEquivalentTo(
                new QueryRowSource[] { new(LeftAlias, item.Message, 1), new("d", item.Target, 1) }, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(row.Redacted).IsFalse();
            await Assert.That(row.RedactedFields!.Value).IsEmpty();
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(ExpectedFields);
            await Assert.That(json.RootElement.GetProperty(MessageIdField).GetString()).IsEqualTo(item.Message);
            await Assert.That(json.RootElement.GetProperty(CustomerNameField).GetString()).IsEqualTo(item.Name);
            await Assert.That(json.RootElement.GetProperty(ScoreField).GetInt32()).IsEqualTo(ExpectedScore);
        }
    }
}
