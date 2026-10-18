using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextProtectedPublicColdMutation
{
    internal static async Task<NativeTextProtectedPublicColdState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await NativeTextClusterScenario.CreateAsync(fixture, token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, token);
        await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
        var patch = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PatchDocument(NativeTextClusterScenario.Collection, NativeTextClusterScenario.FirstId,
                [new(NativeTextClusterScenario.TextField, PatchKind.Set, "\"Львів fresh\"")], 1)]);
        var patched = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(patch, token));
        var delete = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new DeleteDocument(NativeTextClusterScenario.Collection, NativeTextClusterScenario.SecondId, 1)]);
        var deleted = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, delete, token))).Value;
        return new(scenario, identity, patch, patched, delete, deleted);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        NativeTextProtectedPublicColdState state, CancellationToken token)
    {
        foreach (var original in new[] { (state.Patch, state.Patched), (state.Delete, state.Deleted) })
        {
            await Assert.That(original.Item2.CommandId).IsEqualTo(original.Item1.CommandId);
            await Assert.That(original.Item2.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await SqlClusterProtocol.EqualAsync(original.Item2,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original.Item1, token)));
            await SqlClusterProtocol.EqualAsync(original.Item2,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, original.Item1, token))).Value);
        }
    }
}
