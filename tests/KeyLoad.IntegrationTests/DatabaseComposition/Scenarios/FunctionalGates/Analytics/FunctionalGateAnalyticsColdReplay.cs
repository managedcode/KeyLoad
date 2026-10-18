using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using E = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateAnalyticsColdExpected;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsColdReplay
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateAnalyticsColdStore proof,
        CancellationToken token)
    {
        await ReplayAsync(context, proof.Seed, proof.SeedReceipt,
            new(AnalyticsStoreProtocol.UpsertKind, proof.State.Store, proof.State.Third.Id, P.FirstRevision), proof, token);
        await ReplayAsync(context, proof.Update, proof.UpdateReceipt,
            new(AnalyticsStoreProtocol.UpsertKind, proof.State.Store, proof.State.First.Id, P.UpdatedRevision), proof, token);
        await FunctionalGateAnalyticsColdAssertions.VerifyAsync(context, proof, token);
    }

    internal static async Task RepairedAsync(FunctionalGateContext context, FunctionalGateAnalyticsColdStore proof,
        CancellationToken token)
    {
        await FunctionalGateAnalyticsColdAssertions.VerifyAsync(context, proof, token);
        await RefusedOriginalAsync(context, proof, proof.Seed, token);
        await RefusedOriginalAsync(context, proof, proof.Update, token);
    }

    private static async Task RefusedOriginalAsync(FunctionalGateContext context, FunctionalGateAnalyticsColdStore proof,
        CommandRequest command, CancellationToken token)
    {
        var sdk = await context.Writer.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await FunctionalGateAnalyticsColdAssertions.VerifyAsync(context, proof, token);
        await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token), ErrorCode.PermissionDenied, dispatched: true);
        await FunctionalGateAnalyticsColdAssertions.VerifyAsync(context, proof, token);
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command) with
        {
            CommandId = command.CommandId
        };
        var sqlSdk = await context.Writer.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(sqlSdk.IsFailed).IsTrue();
        await Assert.That(sqlSdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(sqlSdk.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await FunctionalGateAnalyticsColdAssertions.VerifyAsync(context, proof, token);
        await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(
            SqlOperationProtocol.ToolName, sql, token), ErrorCode.PermissionDenied, dispatched: true);
        await FunctionalGateAnalyticsColdAssertions.VerifyAsync(context, proof, token);
    }

    internal static async Task ContinueAsync(FunctionalGateContext context, FunctionalGateAnalyticsColdStore proof,
        CancellationToken token)
    {
        var state = proof.State;
        var updated = await FunctionalGateUserAnalyticsWrites.UpsertAsync(context, state, [E.Continued(state)],
            Revisions((state.First.Id, P.UpdatedRevision)), state.First.Id, P.UpdatedRevision, token);
        await Assert.That(updated).IsEqualTo((long)E.ContinuedRevision);
        await FunctionalGateAnalyticsColdAssertions.StateAsync(context, state,
            E.ContinuedRows(state), E.ContinuedAggregate, token);
        var deleted = await FunctionalGateUserAnalyticsWrites.DeleteAsync(context, state,
            Revisions((state.First.Id, E.ContinuedRevision), (state.Second.Id, P.FirstRevision),
                (state.Third.Id, P.FirstRevision)), state.Third.Id, E.ContinuedRevision, token);
        await Assert.That(deleted).IsEqualTo(E.DeletedGeneration);
        await FunctionalGateAnalyticsColdAssertions.StateAsync(context, state, [], new([], P.Empty), token);
    }

    private static async Task ReplayAsync(FunctionalGateContext context, CommandRequest command,
        CommitReceipt expected, MutationReceipt effect, FunctionalGateAnalyticsColdStore proof, CancellationToken token)
    {
        await FunctionalGateCompositionCalls.ReceiptAsync(context, expected, command.CommandId, [effect]);
        await ReceiptAsync(context, await context.SdkAsync(
            context.Writer.Sdk.CommitAsync(FunctionalGateAnalyticsIdentity.Rebuild(command), token)), expected, effect);
        await ReceiptAsync(context, await context.McpAsync<CommitReceipt>(context.Writer,
            McpCallerTools.DocumentsCommit, FunctionalGateAnalyticsIdentity.Wire(command), token), expected, effect);
        await ReceiptAsync(context, await FunctionalGateCompositionCalls.CommandAsync(
            context, FunctionalGateAnalyticsIdentity.Rebuild(command), official: false, token), expected, effect);
        await ReceiptAsync(context, await FunctionalGateCompositionCalls.CommandAsync(
            context, FunctionalGateAnalyticsIdentity.Rebuild(command), official: true, token), expected, effect);
        await FunctionalGateAnalyticsIdentityConflict.VerifyAsync(context, command,
            () => FunctionalGateAnalyticsColdAssertions.VerifyAsync(context, proof, token), token);
        // A changed-content refusal must not replace the original receipt or prevent its next valid public replay.
        await ReceiptAsync(context, await context.SdkAsync(context.Writer.Sdk.CommitAsync(
            FunctionalGateAnalyticsIdentity.Rebuild(command), token)), expected, effect);
    }

    private static async Task ReceiptAsync(FunctionalGateContext context, CommitReceipt actual,
        CommitReceipt expected, MutationReceipt effect)
    {
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(actual, expected);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, actual, expected.CommandId, [effect]);
    }

    private static ImmutableDictionary<string, long> Revisions(params (string Id, long Revision)[] values)
        => values.ToImmutableDictionary(value => value.Id, value => value.Revision, StringComparer.Ordinal);
}
