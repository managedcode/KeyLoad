using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlModelViewParserTests
{
    [Test]
    public async Task AcSqlView001EventAndQueueSourcesLowerToBoundedTypedAst()
    {
        var events = new SqlParser("SELECT e.eventType FROM EVENTS('events', 'stream-a', 3) AS e " +
            "WHERE e.payload.visible = @visible ORDER BY e.revision DESC LIMIT 4", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse();
        var queue = new SqlParser("SELECT q.id, q.state FROM QUEUE_MESSAGES('jobs') q ORDER BY q.id", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse();

        await Assert.That(events.Collection).IsEqualTo(SqlModelViewTestSupport.StreamSet);
        await Assert.That(events.Alias).IsEqualTo("e");
        await Assert.That(events.ModelSource).IsEqualTo(new ModelQuerySource(ModelQuerySourceKind.Events,
            SqlModelViewTestSupport.StreamId, 3));
        await Assert.That(((FieldOperand)((Comparison)events.Filter!).Left).Path).IsEqualTo("/payload/visible");
        await Assert.That(events.Order[0].Path).IsEqualTo("/@revision");
        await Assert.That(queue.Collection).IsEqualTo(SqlModelViewTestSupport.Queue);
        await Assert.That(queue.ModelSource).IsEqualTo(new ModelQuerySource(ModelQuerySourceKind.QueueMessages,
            SqlModelViewTestSupport.Queue));
        await Assert.That(queue.Alias).IsEqualTo("q");
    }

    [Test]
    public async Task AcSqlView001QuotedAndPlainSourceNamesRemainOrdinaryCollections()
    {
        var quoted = new SqlParser("SELECT * FROM \"EVENTS\"", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse();
        var plain = new SqlParser("SELECT * FROM events", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse();

        await Assert.That(quoted.Collection).IsEqualTo("EVENTS");
        await Assert.That(quoted.ModelSource).IsNull();
        await Assert.That(plain.Collection).IsEqualTo("events");
        await Assert.That(plain.ModelSource).IsNull();
    }

    [Test]
    public async Task AcSqlView001RejectsWrongSourceArgumentsAndExtraStatements()
    {
        var secondQueueArgument = Assert.ThrowsExactly<KeyLoadException>(() =>
            new SqlParser("SELECT * FROM QUEUE_MESSAGES('jobs', 'lane-a')", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse());
        var parameterSourceArgument = Assert.ThrowsExactly<KeyLoadException>(() =>
            new SqlParser("SELECT * FROM EVENTS(@set, 'stream-a')", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse());
        var invalidGeneration = Assert.ThrowsExactly<KeyLoadException>(() =>
            new SqlParser("SELECT * FROM EVENTS('events', 'stream-a', 0)", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse());
        var multiStatement = Assert.ThrowsExactly<KeyLoadException>(() =>
            new SqlParser("SELECT * FROM EVENTS('events', 'stream-a'); SELECT * FROM jobs", UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse());

        await Assert.That(secondQueueArgument.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(parameterSourceArgument.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(invalidGeneration.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(multiStatement.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
    }

    [Test]
    public async Task AcSqlView001ModelSourceRoundTripsAsAstJsonWithoutChangingNullSourceShape()
    {
        var model = new AstQueryRequest(new("tenant", "database", "domain", SqlModelViewTestSupport.PartitionKey),
            new("events", null, [new(SqlModelViewTestSupport.EventTypeField, SqlModelViewTestSupport.EventTypeField)], null, [], 10,
                ModelSource: new(ModelQuerySourceKind.Events, "stream-a", 2)), AllowFullScan: true);
        var document = JsonDefaults.Serialize(model);
        var restored = JsonSerializer.Deserialize<AstQueryRequest>(document, JsonDefaults.Options)!;
        var legacy = JsonDefaults.Serialize(new SelectQuery("orders", null, [new("*", "*")], null, [], 10));
        using var legacyDocument = JsonDocument.Parse(legacy);

        await Assert.That(restored.Query.ModelSource).IsEqualTo(model.Query.ModelSource);
        await Assert.That(legacyDocument.RootElement.TryGetProperty(
            SqlModelViewTestSupport.ModelSourceProperty, out _)).IsFalse();
    }
}
