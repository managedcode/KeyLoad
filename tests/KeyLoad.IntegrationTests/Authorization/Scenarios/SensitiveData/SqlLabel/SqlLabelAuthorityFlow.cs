using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SqlLabelAuthorityFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var scenario = await GraphSearchRf3Scenario.CreateAsync(fixture, token);
        var identity = await scenario.CreateReaderAsync(fixture, token);
        var command = Guid.NewGuid();
        var repaired = identity.Principal with
        {
            FieldGrants = [GraphSearchRf3Scenario.LabelFieldGrant],
            PolicyEpoch = SqlLabelAuthorityProtocol.RepairedEpoch
        };
        var state = new SqlLabelAuthorityState(scenario, identity, command, repaired);
        await SqlLabelAuthorityClients.RunAsync(fixture, identity.Secret, async (root, sdk, session) =>
        {
            var image = await SqlLabelAuthorityImage.CaptureAsync(root, scenario.Partition, token);
            await SqlLabelAuthorityRefusals.DeniedAsync(sdk, session, scenario, identity.Secret, token);
            await SqlRf3Protocol.EqualAsync(image, await SqlLabelAuthorityImage.CaptureAsync(root, scenario.Partition, token));
            var granted = await GraphSearchRf3Scenario.GrantLabelUseAsync(fixture, identity, token);
            await SqlRf3Protocol.EqualAsync(identity.Principal with
            {
                FieldGrants = [GraphSearchRf3Scenario.LabelFieldGrant],
                PolicyEpoch = identity.Principal.PolicyEpoch + 1
            }, granted.Principal);
            await SqlLabelAuthorityResults.HealthyAsync(sdk, session, scenario.Partition, token);
            var revoked = granted.Principal with
            {
                Revoked = true,
                PolicyEpoch = SqlLabelAuthorityProtocol.RevokedEpoch
            };
            await SqlRf3Protocol.EqualAsync(revoked,
                await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token)));
            await SqlLabelAuthorityRefusals.RevokedAsync(fixture, sdk, session, state, token);
            await SqlRf3Protocol.EqualAsync(image, await SqlLabelAuthorityImage.CaptureAsync(root, scenario.Partition, token));
            await SqlRf3Protocol.EqualAsync(repaired,
                await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(command, repaired, token)));
            await HealthyAsync(root, sdk, session, state, token);
            await SqlRf3Protocol.EqualAsync(image, await SqlLabelAuthorityImage.CaptureAsync(root, scenario.Partition, token));
        }, token);
        await SqlLabelAuthorityCold.RunAsync(fixture, state, token);
    }

    internal static async Task HealthyAsync(KeyLoadClient root, KeyLoadClient sdk, McpOfficialClient session,
        SqlLabelAuthorityState state, CancellationToken token)
    {
        var before = await SqlLabelAuthorityImage.CaptureAsync(root, state.Scenario.Partition, token);
        await SqlLabelAuthorityResults.HealthyAsync(sdk, session, state.Scenario.Partition, token);
        await SqlRf3Protocol.EqualAsync(state.RepairResult, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(state.RepairCommand, state.RepairResult, token)));
        await SqlRf3Protocol.EqualAsync(before,
            await SqlLabelAuthorityImage.CaptureAsync(root, state.Scenario.Partition, token));
    }
}
