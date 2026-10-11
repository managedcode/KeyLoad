using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochPublicContinuation
{
    internal static async Task<PolicyEpochPublicState> RunAsync(ClusterFixture fixture, KeyLoadClient administrator,
        ResourcePolicyUpdateRf3Scenario scenario, CancellationToken token)
    {
        var seed = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(ResourcePolicyUpdateRf3Protocol.Collection, PolicyEpochPublicProtocol.SecondId,
                PolicyEpochPublicProtocol.SecondJson)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(seed, token));
        await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
        MutationReceipt[] expected = [new(PolicyEpochPublicProtocol.PutKind,
            ResourcePolicyUpdateRf3Protocol.Collection, PolicyEpochPublicProtocol.SecondId,
            PolicyEpochPublicProtocol.SeedRevision)];
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(expected, receipt.Mutations.ToArray());
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            root => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, scenario.Reader.Secret,
                reader => ContinueAsync(root.Sdk, reader, scenario, seed, receipt, token), token), token);
    }

    private static async Task<PolicyEpochPublicState> ContinueAsync(KeyLoadClient root, RequestCqrsRf3Callers reader,
        ResourcePolicyUpdateRf3Scenario scenario, CommandRequest seed, CommitReceipt receipt, CancellationToken token)
    {
        var cursors = await PolicyEpochPublicAssertions.PagesAsync(reader, root, scenario, false, token);
        var state = new PolicyEpochPublicState(scenario, seed, receipt, cursors, scenario.Reader);
        var reduced = await PolicyEpochPublicPolicy.ChangeAsync(root, scenario.Reader, [], false, token);
        state = state with { CurrentReader = reduced };
        await PolicyEpochPublicRefusals.AllAsync(reader, root, state, ErrorCode.CursorExpired, token);
        await PolicyEpochPublicAssertions.PagesAsync(reader, root, scenario, true, token);
        state = state with { CurrentReader = await PolicyEpochPublicPolicy.ChangeAsync(root, reduced, [], true, token) };
        await PolicyEpochPublicRefusals.AllAsync(reader, root, state, ErrorCode.Unauthenticated, token);
        state = state with
        {
            CurrentReader = await PolicyEpochPublicPolicy.ChangeAsync(root, state.CurrentReader,
            scenario.Reader.Principal.FieldGrants, false, token)
        };
        await PolicyEpochPublicRefusals.AllAsync(reader, root, state, ErrorCode.CursorExpired, token);
        await PolicyEpochPublicAssertions.PagesAsync(reader, root, scenario, false, token);
        await PolicyEpochPublicPolicy.ReplayAsync(root, state, token);
        return state;
    }
}
