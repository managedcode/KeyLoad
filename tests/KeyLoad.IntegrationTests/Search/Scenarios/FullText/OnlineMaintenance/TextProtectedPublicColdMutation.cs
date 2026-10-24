using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class TextProtectedPublicColdMutation
{
    internal static async Task<TextProtectedPublicColdState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await TextClusterScenario.CreateAsync(fixture, token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, false, token);
        await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
        var patch = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PatchDocument(TextClusterScenario.Collection, TextClusterScenario.FirstId,
                [new(TextClusterScenario.TextField, PatchKind.Set, "\"Львів fresh\"")], 1)]);
        var patched = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(patch, token));
        var delete = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new DeleteDocument(TextClusterScenario.Collection, TextClusterScenario.SecondId, 1)]);
        var deleted = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, delete, token))).Value;
        return new(scenario, identity, patch, patched, delete, deleted);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        TextProtectedPublicColdState state, CancellationToken token)
    {
        foreach (var original in new[] { (state.Patch, state.Patched), (state.Delete, state.Deleted) })
        {
            await Assert.That(original.Item2.CommandId).IsEqualTo(original.Item1.CommandId);
            await Assert.That(original.Item2.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
            await Assert.That(original.Item2.PersistedCopies).IsEqualTo(3);
            await SqlClusterProtocol.EqualAsync(original.Item2,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original.Item1, token)));
            await SqlClusterProtocol.EqualAsync(original.Item2,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, original.Item1, token))).Value);
        }
    }
}
