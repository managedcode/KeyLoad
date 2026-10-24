using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class TextNestedWildcardPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var state = await TextNestedWildcardPublicScenario.CreateAsync(fixture, administrator, token);
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    state.Identity.Secret, async reader =>
                    {
                        await TextNestedWildcardPublicAssertions.PhaseAsync(reader, administrator, state, 0, token);
                        await PersistAsync(administrator, state.Identity.Principal with
                        {
                            PolicyEpoch = 2L,
                            FieldGrants = [TextNestedWildcardPublicScenario.Use,
                            TextNestedWildcardPublicScenario.ProfileRead]
                        }, token);
                        await TextNestedWildcardPublicAssertions.PhaseAsync(reader, administrator, state, 1, token);
                        var readable = state.Identity.Principal with
                        {
                            PolicyEpoch = 3L,
                            FieldGrants = [TextNestedWildcardPublicScenario.Use,
                                TextNestedWildcardPublicScenario.ProfileRead, TextNestedWildcardPublicScenario.ArrayRead]
                        };
                        await PersistAsync(administrator, readable, token);
                        await TextNestedWildcardPublicAssertions.PhaseAsync(reader, administrator, state, 2, token);
                        await PersistAsync(administrator, readable with
                        {
                            PolicyEpoch = 4L,
                            FieldGrants = [TextNestedWildcardPublicScenario.ProfileRead,
                                TextNestedWildcardPublicScenario.ArrayRead]
                        }, token);
                        await TextNestedWildcardPublicAssertions.DeniedAsync(reader, administrator, state, token);
                        await PersistAsync(administrator, readable with
                        {
                            PolicyEpoch = 5L
                        }, token);
                        await TextNestedWildcardPublicAssertions.PhaseAsync(reader, administrator, state, 2, token);
                        return true;
                    }, token);
            }, token);
    }

    private static async Task PersistAsync(RequestCqrsClusterCallers administrator, PrincipalRecord expected,
        CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
