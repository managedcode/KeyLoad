using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlModelParserOperationFlow
{
    private const int ColdOwners = 2;
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SqlModelParserOperationFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CompleteAsync(SqlModelParserOperationFixture fixture)
    {
        fixture.Initialize();
        _ = fixture.Principal(repaired: false);
        await SqlModelParserRefusals.DeniedAsync(fixture);
        _ = fixture.Principal(repaired: true);
        await ModelSourceArgumentRefusals.RequireAsync(fixture);
        await SqlModelParserRefusals.StaleAsync(fixture);
        await SqlModelParserPages.RequireAsync(fixture);
        await SqlModelParserConservation.ReplayAsync(fixture);
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            var before = SqlModelParserConservation.Capture(fixture);
            fixture.Reopen();
            var after = SqlModelParserConservation.Capture(fixture);
            await Assert.That(after.Image).IsEqualTo(before.Image);
            await Assert.That(after.Position).IsEqualTo(before.Position);
            await ModelSourceArgumentRefusals.RequireAsync(fixture);
            await SqlModelParserRefusals.StaleAsync(fixture);
            await SqlModelParserPages.RequireAsync(fixture);
            await SqlModelParserConservation.ReplayAsync(fixture);
        }
    }
}
