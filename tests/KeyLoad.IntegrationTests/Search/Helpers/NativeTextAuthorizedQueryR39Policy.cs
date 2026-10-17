using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextAuthorizedQueryR39Policy
{
    internal static async Task ApplyAsync(RequestCqrsRf3Callers admin, NativeTextAuthorizedQueryR39State state,
        long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            OwnerId = epoch == 5L ? NativeTextAuthorizedQueryR39Seed.OwnerA : NativeTextAuthorizedQueryR39Seed.OwnerB,
            FieldGrants = epoch == 4L ? [] : epoch == 3L
                ? [NativeTextAuthorizedQueryR39Seed.UseGrant] : state.Identity.Principal.FieldGrants
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
