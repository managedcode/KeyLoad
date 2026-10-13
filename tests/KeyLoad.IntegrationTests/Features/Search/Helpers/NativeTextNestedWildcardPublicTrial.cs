using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextNestedWildcardPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var state = await NativeTextNestedWildcardPublicScenario.CreateAsync(fixture, administrator, token);
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    state.Identity.Secret, async reader =>
                    {
                        await NativeTextNestedWildcardPublicAssertions.PhaseAsync(reader, administrator, state, 0, token);
                        await PersistAsync(administrator, state.Identity.Principal with
                        {
                            PolicyEpoch = 2L,
                            FieldGrants = [NativeTextNestedWildcardPublicScenario.Use,
                            NativeTextNestedWildcardPublicScenario.ProfileRead]
                        }, token);
                        await NativeTextNestedWildcardPublicAssertions.PhaseAsync(reader, administrator, state, 1, token);
                        var readable = state.Identity.Principal with
                        {
                            PolicyEpoch = 3L,
                            FieldGrants = [NativeTextNestedWildcardPublicScenario.Use,
                                NativeTextNestedWildcardPublicScenario.ProfileRead, NativeTextNestedWildcardPublicScenario.ArrayRead]
                        };
                        await PersistAsync(administrator, readable, token);
                        await NativeTextNestedWildcardPublicAssertions.PhaseAsync(reader, administrator, state, 2, token);
                        await PersistAsync(administrator, readable with
                        {
                            PolicyEpoch = 4L,
                            FieldGrants = [NativeTextNestedWildcardPublicScenario.ProfileRead,
                                NativeTextNestedWildcardPublicScenario.ArrayRead]
                        }, token);
                        await NativeTextNestedWildcardPublicAssertions.DeniedAsync(reader, administrator, state, token);
                        await PersistAsync(administrator, readable with { PolicyEpoch = 5L }, token);
                        await NativeTextNestedWildcardPublicAssertions.PhaseAsync(reader, administrator, state, 2, token);
                        return true;
                    }, token);
            }, token);
    }

    private static async Task PersistAsync(RequestCqrsRf3Callers administrator, PrincipalRecord expected,
        CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
