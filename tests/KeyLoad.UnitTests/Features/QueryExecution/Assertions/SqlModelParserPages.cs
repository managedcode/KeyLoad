using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlModelParserPages
{
    private const long FirstRevision = 1;
    private const long SecondRevision = 2;
    private const string FirstEventJson = "{\"eventType\":\"Created\"}";
    private const string SecondEventJson = "{\"eventType\":\"Updated\"}";
    private const string FirstQueueJson = "{\"id\":\"message-a\",\"state\":\"Ready\"}";
    private const string SecondQueueJson = "{\"id\":\"message-b\",\"state\":\"Ready\"}";

    internal static async Task RequireAsync(SqlModelParserOperationFixture fixture)
    {
        var before = SqlModelParserConservation.Capture(fixture);
        var engine = new QueryEngine(fixture.Owner, UnitExecutionOptions.QueryExecution());
        var events = new QueryPage([new(SqlModelParserOperationData.SecondEvent, SecondRevision, SecondEventJson, false, []),
            new(SqlModelViewTestSupport.EventId, FirstRevision, FirstEventJson, false, [])], null, before.Position, SqlModelParserOperationData.EventPath);
        var queue = new QueryPage([new(SqlModelParserOperationData.SecondMessage, FirstRevision, SecondQueueJson, false, []),
            new(SqlModelViewTestSupport.MessageId, FirstRevision, FirstQueueJson, false, [])], null, before.Position, SqlModelParserOperationData.QueuePath);
        await PageAsync(engine.Execute(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.EventSql, AllowFullScan: true)), events);
        await SqlModelParserConservation.SameAsync(fixture, before);
        await PageAsync(engine.ExecuteAst(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.Events(), AllowFullScan: true)), events);
        await SqlModelParserConservation.SameAsync(fixture, before);
        await PageAsync(engine.Execute(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.QueueSql, AllowFullScan: true)), queue);
        await SqlModelParserConservation.SameAsync(fixture, before);
        await PageAsync(engine.ExecuteAst(SqlModelParserOperationData.Reader,
            new(fixture.Partition, SqlModelParserOperationData.Queue, AllowFullScan: true)), queue);
        await SqlModelParserConservation.SameAsync(fixture, before);
    }
    private static async Task PageAsync(QueryPage actual, QueryPage expected)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
}
