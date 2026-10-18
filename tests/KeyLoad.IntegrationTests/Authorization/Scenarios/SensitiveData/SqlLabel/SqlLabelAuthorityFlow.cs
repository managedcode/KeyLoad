using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SqlLabelAuthorityFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var scenario = await GraphSearchClusterScenario.CreateAsync(fixture, token);
        var identity = await scenario.CreateReaderAsync(fixture, token);
        var command = Guid.NewGuid();
        var repaired = identity.Principal with
        {
            FieldGrants = [GraphSearchClusterScenario.LabelFieldGrant],
            PolicyEpoch = SqlLabelAuthorityProtocol.RepairedEpoch
        };
        var state = new SqlLabelAuthorityState(scenario, identity, command, repaired);
        await SqlLabelAuthorityClients.RunAsync(fixture, identity.Secret, async (root, sdk, session) =>
        {
            var image = await SqlLabelAuthorityImage.CaptureAsync(root, scenario.Partition, token);
            await SqlLabelAuthorityRefusals.DeniedAsync(sdk, session, scenario, identity.Secret, token);
            await SqlClusterProtocol.EqualAsync(image, await SqlLabelAuthorityImage.CaptureAsync(root, scenario.Partition, token));
            var granted = await GraphSearchClusterScenario.GrantLabelUseAsync(fixture, identity, token);
            await SqlClusterProtocol.EqualAsync(identity.Principal with
            {
                FieldGrants = [GraphSearchClusterScenario.LabelFieldGrant],
                PolicyEpoch = identity.Principal.PolicyEpoch + 1
            }, granted.Principal);
            await SqlLabelAuthorityResults.HealthyAsync(sdk, session, scenario.Partition, token);
            var revoked = granted.Principal with
            {
                Revoked = true,
                PolicyEpoch = SqlLabelAuthorityProtocol.RevokedEpoch
            };
            await SqlClusterProtocol.EqualAsync(revoked,
                await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token)));
            await SqlLabelAuthorityRefusals.RevokedAsync(fixture, sdk, session, state, token);
            await SqlClusterProtocol.EqualAsync(image, await SqlLabelAuthorityImage.CaptureAsync(root, scenario.Partition, token));
            await SqlClusterProtocol.EqualAsync(repaired,
                await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(command, repaired, token)));
            await HealthyAsync(root, sdk, session, state, token);
            await SqlClusterProtocol.EqualAsync(image, await SqlLabelAuthorityImage.CaptureAsync(root, scenario.Partition, token));
        }, token);
        await SqlLabelAuthorityCold.RunAsync(fixture, state, token);
    }

    internal static async Task HealthyAsync(KeyLoadClient root, KeyLoadClient sdk, McpOfficialClient session,
        SqlLabelAuthorityState state, CancellationToken token)
    {
        var before = await SqlLabelAuthorityImage.CaptureAsync(root, state.Scenario.Partition, token);
        await SqlLabelAuthorityResults.HealthyAsync(sdk, session, state.Scenario.Partition, token);
        await SqlClusterProtocol.EqualAsync(state.RepairResult, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(state.RepairCommand, state.RepairResult, token)));
        await SqlClusterProtocol.EqualAsync(before,
            await SqlLabelAuthorityImage.CaptureAsync(root, state.Scenario.Partition, token));
    }
}
