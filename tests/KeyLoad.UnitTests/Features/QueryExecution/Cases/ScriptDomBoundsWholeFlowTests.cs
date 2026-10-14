namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class ScriptDomBoundsWholeFlowTests
{
    [Test]
    public async Task AcTsql004OriginalBoundsRefuseWithoutEffectsThenHealthyReplayAndTwoColdOwners()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(new()
            { MaxQueryBytes = ByteBound, MaxQueryTokens = TokenBound, MaxQueryDepth = DepthBound });
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var (seed, receipt) = SqlOffsetFixture.Seed(database);
                await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.GrantEpoch, true);
                await ScriptDomBoundsControls.RequireAsync(database);
                await ScriptDomNativeParsingCancellation.RequireAsync(database,
                    new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution()));
                await ScriptDomMoneyModePages.RequireAsync(database);
                await SqlOffsetPages.ReplayAsync(database.Database, seed, receipt);
                await ScriptDomSelectCold.RunAsync(database, seed, receipt);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
    internal const int ByteBound = 4096;
    internal const int TokenBound = 128;
    internal const int DepthBound = 16;
}
