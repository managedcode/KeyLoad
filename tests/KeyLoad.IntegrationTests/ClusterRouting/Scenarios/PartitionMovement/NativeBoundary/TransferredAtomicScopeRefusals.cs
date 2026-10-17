using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TransferredAtomicScopeRefusals
{
    internal static async Task<TransferredAtomicScopeState> CreateAndRequireAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var state = new TransferredAtomicScopeState(TransferredAtomicScopeCommands.Create(seed),
            await TransferredAtomicScopeCurrent.OutboxAsync(seed, token));
        await TransferredAtomicScopeCurrent.RequireAsync(seed, state, token);
        await ReplayAsync(seed, state, token);
        return state;
    }
    internal static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState state, CancellationToken token)
    {
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            var refused = await sdk.CommitAsync(state.Refused, token);
            await Assert.That(refused.IsSuccess).IsFalse();
            await Assert.That(refused.Value).IsNull();
            await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(refused.Problem, ErrorCode.Conflict,
                TransferredAtomicScopeCommands.UniqueDetail);
            await TransferredAtomicScopeCurrent.RequireAsync(seed, state, token);
        }
        var reply = await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, state.Refused, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Conflict, dispatched: true);
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.Conflict,
            TransferredAtomicScopeCommands.UniqueDetail), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error), expected)).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, seed.Credential, TransferredAtomicScopeCommands.UniqueJson);
        await TransferredAtomicScopeCurrent.RequireAsync(seed, state, token);
    }
}
