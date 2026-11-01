namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateReadPositions
{
    internal static async Task RequireAsync(FunctionalGateContext context, long canonicalMinimum,
        CancellationToken token, params long[] physicalCuts)
    {
        var status = await context.SdkAsync(context.Administrator.Sdk.StatusAsync(token));
        await Assert.That(status.Position).IsGreaterThanOrEqualTo(canonicalMinimum);
        foreach (var cut in physicalCuts)
        {
            await Assert.That(cut).IsGreaterThan(0L);
        }
    }
}
