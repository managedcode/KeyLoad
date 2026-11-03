using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class JoinCapabilityWholeFlowTests
{
    [Test]
    public async Task JsonAndAstRepresentationsExecuteCompleteLiteralJoin()
    {
        using var database = new TestDatabase();
        SqlInnerJoinRejectionFixture.Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = JoinCapabilityFixtures.Healthy(database);
        var json = JoinCapabilityFixtures.JsonRoundTrip(request);
        var execution = JoinCapabilityFixtures.RuntimeRoundTrip(request);
        await Assert.That(JsonDefaults.Serialize(json).AsSpan().SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(execution).AsSpan().SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        await JoinCapabilityOperation.VerifyAsync(database, engine, json);
        await JoinCapabilityOperation.VerifyAsync(database, engine, execution);
    }

    [Test]
    [Arguments("filter")]
    [Arguments("explain")]
    [Arguments("alias")]
    [Arguments("self")]
    public async Task RoundtrippedUnsupportedShapeDoesNoReadAndFollowingJoinCompletes(string kind)
    {
        using var database = new TestDatabase();
        SqlInnerJoinRejectionFixture.Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var healthy = JoinCapabilityFixtures.Healthy(database);
        var unsupported = JoinCapabilityFixtures.Unsupported(healthy, kind);
        await JoinCapabilityOperation.RejectAsync(database, engine, JoinCapabilityFixtures.JsonRoundTrip(unsupported));
        await JoinCapabilityOperation.RejectAsync(database, engine, JoinCapabilityFixtures.RuntimeRoundTrip(unsupported));
        await JoinCapabilityOperation.VerifyAsync(database, engine, JoinCapabilityFixtures.JsonRoundTrip(healthy));
    }
}
