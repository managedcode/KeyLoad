using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochPublicContinuation
{
    internal static async Task<PolicyEpochPublicState> RunAsync(ClusterFixture fixture, KeyLoadClient administrator,
        ResourcePolicyUpdateClusterScenario scenario, CancellationToken token)
    {
        var seed = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(ResourcePolicyUpdateClusterProtocol.Collection, PolicyEpochPublicProtocol.SecondId,
                PolicyEpochPublicProtocol.SecondJson)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(seed, token));
        await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
        MutationReceipt[] expected = [new(PolicyEpochPublicProtocol.PutKind,
            ResourcePolicyUpdateClusterProtocol.Collection, PolicyEpochPublicProtocol.SecondId,
            PolicyEpochPublicProtocol.SeedRevision)];
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlClusterProtocol.EqualAsync(expected, receipt.Mutations.ToArray());
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            root => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, scenario.Reader.Secret,
                reader => ContinueAsync(fixture, root.Sdk, reader, scenario, seed, receipt, token), token), token);
    }

    private static async Task<PolicyEpochPublicState> ContinueAsync(ClusterFixture fixture, KeyLoadClient root, RequestCqrsClusterCallers reader,
        ResourcePolicyUpdateClusterScenario scenario, CommandRequest seed, CommitReceipt receipt, CancellationToken token)
    {
        var cursors = await PolicyEpochPublicAssertions.PagesAsync(reader, root, scenario, false, token);
        var state = new PolicyEpochPublicState(scenario, seed, receipt, cursors, scenario.Reader);
        var reduced = await PolicyEpochPublicPolicy.ChangeAsync(root, scenario.Reader, [], false, token);
        state = state with
        {
            CurrentReader = reduced
        };
        await PolicyEpochPublicRefusals.AllAsync(fixture, reader, root, state, ErrorCode.CursorExpired, token);
        await PolicyEpochPublicAssertions.PagesAsync(reader, root, scenario, true, token);
        state = state with
        {
            CurrentReader = await PolicyEpochPublicPolicy.ChangeAsync(root, reduced, [], true, token)
        };
        await PolicyEpochPublicRefusals.AllAsync(fixture, reader, root, state, ErrorCode.Unauthenticated, token);
        state = state with
        {
            CurrentReader = await PolicyEpochPublicPolicy.ChangeAsync(root, state.CurrentReader,
            scenario.Reader.Principal.FieldGrants, false, token)
        };
        await PolicyEpochPublicRefusals.AllAsync(fixture, reader, root, state, ErrorCode.CursorExpired, token);
        await PolicyEpochPublicAssertions.PagesAsync(reader, root, scenario, false, token);
        await PolicyEpochPublicPolicy.ReplayAsync(root, state, token);
        await PolicyEpochAuthorityOutage.RunAsync(fixture, reader, root, state, token);
        return state;
    }
}
