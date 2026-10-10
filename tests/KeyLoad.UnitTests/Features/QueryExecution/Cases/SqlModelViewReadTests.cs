using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlModelViewReadTests
{
    [Test]
    public async Task AcSqlView002SqlAndAstReadTheSameEventAndQueueRowsWithoutMutation()
    {
        using var database = SqlModelViewTestSupport.Create();
        SqlModelViewTestSupport.Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var eventBefore = database.Database.ReadStream("root",
            new StreamRef(database.Partition, SqlModelViewTestSupport.StreamSet, SqlModelViewTestSupport.StreamId, 1));
        var eventBeforeBytes = NativeSerialization.Serialize(eventBefore);
        var queueLane = new QueueLaneRef(database.Partition, SqlModelViewTestSupport.Queue);
        var queueBefore = database.Database.InspectMessage("root", queueLane, SqlModelViewTestSupport.MessageId);

        var eventSql = engine.Execute("root", SqlModelViewTestSupport.Request(database,
            "SELECT * FROM EVENTS('events', 'stream-a')"));
        var eventAst = engine.ExecuteAst("root", new(database.Partition,
            new(SqlModelViewTestSupport.StreamSet, null, [new("*", "*")], null, [], 10,
                ModelSource: new(ModelQuerySourceKind.Events, SqlModelViewTestSupport.StreamId)), AllowFullScan: true));
        var queueSql = engine.Execute("root", SqlModelViewTestSupport.Request(database,
            "SELECT * FROM QUEUE_MESSAGES('jobs')"));
        var queueAst = engine.ExecuteAst("root", new(database.Partition,
            new(SqlModelViewTestSupport.Queue, null, [new("*", "*")], null, [], 10,
                ModelSource: new(ModelQuerySourceKind.QueueMessages, SqlModelViewTestSupport.Queue)), AllowFullScan: true));

        var eventRow = await Assert.That(eventSql.Rows).HasSingleItem();
        var queueRow = await Assert.That(queueSql.Rows).HasSingleItem();
        using var eventJson = JsonDocument.Parse(eventRow.Json);
        using var queueJson = JsonDocument.Parse(queueRow.Json);
        await Assert.That(eventRow.EntityId).IsEqualTo(SqlModelViewTestSupport.EventId);
        await Assert.That(eventRow.Revision).IsEqualTo(1);
        await Assert.That(eventJson.RootElement.GetProperty(SqlModelViewTestSupport.EventTypeField).GetString()).IsEqualTo("Created");
        await Assert.That(eventJson.RootElement.GetProperty(SqlModelViewTestSupport.PayloadField)
                .GetProperty(SqlModelViewTestSupport.VisibleField).GetString())
            .IsEqualTo("event-visible");
        await Assert.That(queueRow.EntityId).IsEqualTo(SqlModelViewTestSupport.MessageId);
        await Assert.That(queueJson.RootElement.GetProperty(SqlModelViewTestSupport.QueueStateField).GetString()).IsEqualTo("Ready");
        await Assert.That(queueJson.RootElement.GetProperty(SqlModelViewTestSupport.AttemptsField).GetInt32()).IsEqualTo(0);
        await Assert.That(queueRow.Json).DoesNotContain("lease");
        await Assert.That(queueRow.Json).DoesNotContain("deliveryGeneration");
        await Assert.That(queueRow.Json).DoesNotContain("fingerprint");
        await AssertRowsMatch(eventSql.Rows, eventAst.Rows);
        await AssertRowsMatch(queueSql.Rows, queueAst.Rows);
        await Assert.That(eventSql.Cursor).IsNull();
        await Assert.That(queueSql.Cursor).IsNull();

        var eventAfter = database.Database.ReadStream("root",
            new StreamRef(database.Partition, SqlModelViewTestSupport.StreamSet, SqlModelViewTestSupport.StreamId, 1));
        var queueAfter = database.Database.InspectMessage("root", queueLane, SqlModelViewTestSupport.MessageId);
        await Assert.That(NativeSerialization.Serialize(eventAfter).SequenceEqual(eventBeforeBytes)).IsTrue();
        await Assert.That(queueAfter).IsEqualTo(queueBefore);
    }

    private static async Task AssertRowsMatch(ImmutableArray<QueryRow> expected, ImmutableArray<QueryRow> actual)
    {
        await Assert.That(actual.IsDefault).IsEqualTo(expected.IsDefault);
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var expectedRow = expected[index];
            var actualRow = actual[index];
            await Assert.That(actualRow.EntityId).IsEqualTo(expectedRow.EntityId);
            await Assert.That(actualRow.Revision).IsEqualTo(expectedRow.Revision);
            await Assert.That(actualRow.Json).IsEqualTo(expectedRow.Json);
            await Assert.That(actualRow.Redacted).IsEqualTo(expectedRow.Redacted);
            await Assert.That(actualRow.RedactedFields.HasValue).IsEqualTo(expectedRow.RedactedFields.HasValue);
            await Assert.That(actualRow.RedactedFields?.IsDefault).IsEqualTo(expectedRow.RedactedFields?.IsDefault);
            await Assert.That(actualRow.RedactedFields?.ToArray() ?? [])
                .IsEquivalentTo(expectedRow.RedactedFields?.ToArray() ?? [], CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task AcSqlView003FilterOrderLimitAndProjectionUseTheSharedQueryOperators()
    {
        using var database = SqlModelViewTestSupport.Create();
        database.Commit(
            new AppendEvents(SqlModelViewTestSupport.StreamSet, SqlModelViewTestSupport.StreamId,
                [new("event-a", "Created", PayloadJson: "{\"score\":1}")], ExpectedStreamRevision.NoStream),
            new AppendEvents(SqlModelViewTestSupport.StreamSet, "stream-b",
                [new("event-b", "Updated", PayloadJson: "{\"score\":2}")], ExpectedStreamRevision.NoStream));
        SqlModelViewTestSupport.Principal(database, SqlModelViewTestSupport.Reader,
            Capability.Query | Capability.EventsRead, Capability.None);
        var request = new AstQueryRequest(database.Partition,
            new(SqlModelViewTestSupport.StreamSet, "e", [new("/payload/score", "score")],
                new Comparison(new FieldOperand("/payload/score"), ">", ValueOperand.Create(JsonSerializer.SerializeToElement(1))),
                [new("/payload/score", true)], 1,
                ModelSource: new(ModelQuerySourceKind.Events, "stream-b")), AllowFullScan: true);
        var page = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()).ExecuteAst(SqlModelViewTestSupport.Reader, request);

        var row = await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(row.EntityId).IsEqualTo("event-b");
        await Assert.That(row.Json).Contains("\"score\":2");
        await Assert.That(page.Cursor).IsNull();
    }
}
