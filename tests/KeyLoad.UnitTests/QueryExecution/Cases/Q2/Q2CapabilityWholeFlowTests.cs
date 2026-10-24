using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class Q2CapabilityWholeFlowTests
{
    [Test]
    public async Task PreservedJsonAndAst2FixturesExecuteCompleteLiteralJoin()
    {
        using var database = new TestDatabase();
        SqlInnerJoinRejectionFixture.Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = Q2CapabilityFixtures.Healthy(database);
        var json = Q2CapabilityFixtures.JsonRoundTrip(request);
        var execution = Q2CapabilityFixtures.RuntimeRoundTrip(request);
        await Assert.That(JsonDefaults.Serialize(json).AsSpan().SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(execution).AsSpan().SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        await Q2CapabilityOperation.VerifyAsync(database, engine, json);
        await Q2CapabilityOperation.VerifyAsync(database, engine, execution);
    }

    [Test]
    [Arguments("ast-other")]
    [Arguments("ast3")]
    [Arguments("filter")]
    [Arguments("explain")]
    [Arguments("alias")]
    [Arguments("self")]
    public async Task RoundtrippedUnsupportedProfileDoesNoReadAndFollowingAst2Completes(string kind)
    {
        using var database = new TestDatabase();
        SqlInnerJoinRejectionFixture.Seed(database);
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var healthy = Q2CapabilityFixtures.Healthy(database);
        var unsupported = Q2CapabilityFixtures.Unsupported(healthy, kind);
        await Q2CapabilityOperation.RejectAsync(database, engine, Q2CapabilityFixtures.JsonRoundTrip(unsupported));
        await Q2CapabilityOperation.RejectAsync(database, engine, Q2CapabilityFixtures.RuntimeRoundTrip(unsupported));
        await Q2CapabilityOperation.VerifyAsync(database, engine, Q2CapabilityFixtures.JsonRoundTrip(healthy));
    }
}
