namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartialReadCancellationR45Trial
{
    internal static async Task RunAsync(BlobPublicCancellationOwner owner, bool useMcp,
        BlobMetadata previous, CancellationToken token)
    {
        var state = await BlobPartialReadCancellationR45Seed.CreateAsync(owner, previous, token);
        await BlobPartialReadCancellationR45Pages.HealthyAsync(owner, state, token);
        await BlobPartialReadCancellationR45Held.RunAsync(owner, useMcp, state, token);
        await BlobPartialReadCancellationR45Pages.HealthyAsync(owner, state, token);
        await BlobBoundedRetirementPublicReplay.PublicationAsync(owner.Caller, state.Publication,
            () => BlobPartialReadCancellationR45Pages.CurrentAsync(owner.Administrator, state, token), token);
    }
}
