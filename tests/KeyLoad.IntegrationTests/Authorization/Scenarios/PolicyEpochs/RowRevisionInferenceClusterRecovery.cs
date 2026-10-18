using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceClusterRecovery
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        RowRevisionInferenceClusterState state, string kind, CancellationToken token)
    {
        var repair = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(P.Collection, P.Hidden, P.HiddenJson, P.OriginalRevision,
                new(OwnerId: P.Reader), ExplicitReplacement: true)]);
        var repaired = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(repair, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(repaired, repair, [new(P.PutReceipt, P.Collection, P.Hidden, P.RepairRevision)]);
        state.HiddenRevision = P.RepairRevision;
        state.RootOriginals.Add((repair, repaired));
        await RowRevisionInferenceClusterCurrent.CaptureOutboxAsync(root, state, P.RepairRecords, token);
        await RowRevisionInferenceClusterCurrent.RequireAsync(root, state, token);
        await FreshAsync(root, reader, state, kind, token);
        await RowRevisionInferenceClusterAttempts.ReplayAsync(root, reader, state, token);
        await RowRevisionInferenceClusterCurrent.RootReplayAsync(root, state, token);
        await ReadsAsync(root, reader, state, token);
    }

    private static async Task FreshAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        RowRevisionInferenceClusterState state, string kind, CancellationToken token)
    {
        var command = RowRevisionInferenceClusterRequests.Create(state.Partition, kind, P.Hidden, P.RepairRevision);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(RowRevisionInferenceClusterRequests.ReceiptKind(kind), P.Collection, P.Hidden, P.HealthyRevision)]);
        state.HiddenRevision = P.HealthyRevision;
        state.HiddenDeleted = kind == P.Delete;
        state.HiddenJson = kind == P.Patch ? P.PatchedJson : P.Changed;
        state.HealthyOriginal = (command, receipt);
        await RowRevisionInferenceClusterCurrent.CaptureOutboxAsync(root, state, P.HealthyRecords, token);
        await RowRevisionInferenceClusterCurrent.RequireAsync(root, state, token);
        await ReplayAsync(root, reader, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        RowRevisionInferenceClusterState state, CancellationToken token)
    {
        var item = state.HealthyOriginal;
        await SqlClusterProtocol.EqualAsync(item.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, item.Command, token))).Value);
        await RowRevisionInferenceClusterCurrent.RequireAsync(root, state, token);
        await SqlClusterProtocol.EqualAsync(item.Receipt, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(item.Command, token)));
        await RowRevisionInferenceClusterCurrent.RequireAsync(root, state, token);
    }

    internal static async Task ReadsAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        RowRevisionInferenceClusterState state, CancellationToken token)
    {
        foreach (var id in new[] { P.Hidden, P.Own })
        {
            var reference = new EntityRef(state.Partition, P.Collection, id);
            DocumentReadResult expected = new(id == P.Hidden && state.HiddenDeleted ? null
                : new(reference, id == P.Hidden ? P.HealthyRevision : P.OriginalRevision,
                    id == P.Hidden ? state.HiddenJson : P.Original, false, []));
            await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.GetAsync(reference, token)));
            await RowRevisionInferenceClusterCurrent.RequireAsync(root, state, token);
            await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
            await RowRevisionInferenceClusterCurrent.RequireAsync(root, state, token);
        }
    }
}
