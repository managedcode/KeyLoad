using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicMembershipScopeReplay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers caller,
        AtomicMembershipScopeState state, CancellationToken token)
    {
        var originals = new[]
        {
            (state.First, state.FirstReceipt),
            (state.Second, state.SecondReceipt)
        };
        foreach (var original in originals)
        {
            await SqlRf3Protocol.EqualAsync(original.Item2,
                await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(original.Item1, token)));
            await CurrentAsync(admin, caller, state, token);
            await SqlRf3Protocol.EqualAsync(original.Item2,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, original.Item1, token))).Value);
            await CurrentAsync(admin, caller, state, token);
        }
        var changed = state.First with
        {
            Mutations = state.Second.Mutations
        };
        var refused = await caller.Sdk.CommitAsync(changed, token);
        await Assert.That(refused.IsSuccess).IsFalse();
        await Assert.That(refused.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Conflict,
            AtomicMembershipScopeCommands.ConflictDetail), refused.Problem);
        await CurrentAsync(admin, caller, state, token);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await caller.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, changed, token), ErrorCode.Conflict,
            AtomicMembershipScopeCommands.ConflictDetail);
        await CurrentAsync(admin, caller, state, token);
    }

    private static Task CurrentAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers caller,
        AtomicMembershipScopeState state, CancellationToken token)
        => AtomicMembershipScopeCurrent.RunAsync(admin, caller, state.Identity, state.Placement,
            2, AtomicMembershipScopeCommands.UpdatedJson, state.Outbox, token);
}
