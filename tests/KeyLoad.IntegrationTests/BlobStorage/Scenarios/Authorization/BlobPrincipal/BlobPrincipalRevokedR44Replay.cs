using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPrincipalRevokedR44Replay
{
    internal static Task RunAsync(RequestCqrsClusterCallers admin, BlobPrincipalRevokedR44State state, CancellationToken token)
        => BlobBoundedRetirementPublicReplay.PublicationAsync(admin, state.Published,
            () => BlobPrincipalRevokedR44Pages.CurrentAsync(admin, state, token), token);
}
