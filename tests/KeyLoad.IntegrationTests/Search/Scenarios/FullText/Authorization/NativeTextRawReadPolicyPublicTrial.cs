using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRawReadPolicyPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var scenario = await NativeTextClusterScenario.CreateAsync(fixture, token);
                var identity = await scenario.CreateReaderAsync(fixture, true, true, false, true, token);
                await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
                await Assert.That(identity.Principal.RestrictRows).IsTrue();
                await Assert.That(identity.Principal.OwnerId).IsEqualTo(NativeTextClusterScenario.OwnerA);
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    identity.Secret, async reader =>
                    {
                        await NativeTextRawReadPolicyPublicAssertions.PhaseAsync(reader, administrator, scenario, false, token);
                        await PersistAsync(administrator, identity.Principal with
                        {
                            FieldGrants = identity.Principal.FieldGrants.Add(NativeTextClusterScenario.SecretReadGrant),
                            PolicyEpoch = 2L
                        }, token);
                        await NativeTextRawReadPolicyPublicAssertions.PhaseAsync(reader, administrator, scenario, true, token);
                        await PersistAsync(administrator, identity.Principal with
                        {
                            PolicyEpoch = 3L
                        }, token);
                        await NativeTextRawReadPolicyPublicAssertions.PhaseAsync(reader, administrator, scenario, false, token);
                        return true;
                    }, token);
            }, token);
    }

    private static async Task PersistAsync(RequestCqrsClusterCallers administrator,
        PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
}
