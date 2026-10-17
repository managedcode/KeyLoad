using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextVectorFusionR40Policy
{
    internal static async Task ApplyAsync(RequestCqrsRf3Callers admin, NativeTextVectorFusionR40State state,
        long epoch, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = epoch,
            RestrictRows = epoch is 3L or 4L,
            OwnerId = epoch is 3L or 4L ? NativeTextRf3Scenario.OwnerB : NativeTextRf3Scenario.OwnerA,
            FieldGrants = epoch == 2L ? [NativeTextVectorFusionR40Seed.RawReadGrant, NativeTextRf3Scenario.SecretReadGrant, NativeTextRf3Scenario.TextUseGrant]
                : epoch == 4L ? [NativeTextVectorFusionR40Seed.RawReadGrant, NativeTextRf3Scenario.SecretReadGrant, NativeTextRf3Scenario.VectorUseGrant]
                : state.Reader.Principal.FieldGrants
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
