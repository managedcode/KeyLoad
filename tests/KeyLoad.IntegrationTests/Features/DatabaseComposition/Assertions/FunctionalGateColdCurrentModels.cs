namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdCurrentModels
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateColdState original,
        FunctionalGateColdDropState dropped, CancellationToken token)
    {
        foreach (var model in original.Models)
        {
            await FunctionalGateColdReadAssertions.VerifyRecreatedAsync(context, model,
                () => FunctionalGateColdDrop.VerifyTableAsync(context, model, dropped, token), token).ConfigureAwait(false);
        }
    }
}
