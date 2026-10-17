using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PartitionAuthorityFlow
{
    internal static async Task<PartitionAuthorityState> WarmAsync(ClusterFixture fixture,
        PartitionQueryRf3Scenario scenario, McpPersistedIdentity identity, Func<Task> originalHealthy, CancellationToken token)
    {
        var restored = identity.Principal with
        {
            Grants = identity.Principal.Grants.Add(new(scenario.Partitions[PartitionAuthorityProtocol.SecondLeaf].DatabaseId,
                PartitionQueryRf3Protocol.Collection, Capability.DocumentsRead | Capability.Query)),
            PolicyEpoch = PartitionAuthorityProtocol.FinalEpoch
        };
        var state = new PartitionAuthorityState(scenario, identity, Guid.NewGuid(), restored);
        await PartitionAuthorityClients.RunAsync(fixture, identity.Secret, async (root, sdk, session) =>
        {
            var image = await PartitionAuthorityImage.CaptureAsync(root, state, token);
            await PartitionAuthorityRefusals.ReadAsync(sdk, session, state, ErrorCode.PermissionDenied, token);
            await originalHealthy();
            await SqlRf3Protocol.EqualAsync(image, await PartitionAuthorityImage.CaptureAsync(root, state, token));
            await PersistAsync(root, Guid.NewGuid(), restored with
            {
                PolicyEpoch = PartitionAuthorityProtocol.RepairEpoch
            }, token);
            await PartitionAuthorityResults.HealthyAsync(sdk, session, state, PartitionAuthorityProtocol.RepairEpoch, token);
            await PersistAsync(root, Guid.NewGuid(), identity.Principal with
            {
                PolicyEpoch = PartitionAuthorityProtocol.ReducedEpoch
            }, token);
            await PartitionAuthorityRefusals.ReadAsync(sdk, session, state, ErrorCode.PermissionDenied, token);
            await SqlRf3Protocol.EqualAsync(image, await PartitionAuthorityImage.CaptureAsync(root, state, token));
            await PersistAsync(root, Guid.NewGuid(), restored with
            {
                Revoked = true,
                PolicyEpoch = PartitionAuthorityProtocol.RevokedEpoch
            }, token);
            await PartitionAuthorityRefusals.ReadAsync(sdk, session, state, ErrorCode.Unauthenticated, token);
            await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, identity.Secret, token);
            await SqlRf3Protocol.EqualAsync(image, await PartitionAuthorityImage.CaptureAsync(root, state, token));
            await PersistAsync(root, state.RepairCommand, restored, token);
            await HealthyAsync(root, sdk, session, state, token);
            await SqlRf3Protocol.EqualAsync(image, await PartitionAuthorityImage.CaptureAsync(root, state, token));
        }, token);
        return state;
    }

    internal static async Task HealthyAsync(KeyLoadClient root, KeyLoadClient sdk, McpOfficialClient session,
        PartitionAuthorityState state, CancellationToken token)
    {
        var before = await PartitionAuthorityImage.CaptureAsync(root, state, token);
        await PartitionAuthorityResults.HealthyAsync(sdk, session, state, PartitionAuthorityProtocol.FinalEpoch, token);
        await PersistAsync(root, state.RepairCommand, state.RepairResult, token);
        await SqlRf3Protocol.EqualAsync(before, await PartitionAuthorityImage.CaptureAsync(root, state, token));
    }

    private static async Task PersistAsync(KeyLoadClient root, Guid command, PrincipalRecord principal, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(principal,
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(command, principal, token)));
}
