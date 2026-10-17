using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceRf3Recovery
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, string kind, CancellationToken token)
    {
        var repair = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(P.Collection, P.Hidden, P.HiddenJson, P.OriginalRevision,
                new(OwnerId: P.Reader), ExplicitReplacement: true)]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(P.PlacementVersion, state.Partition), token));
        var repaired = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(repair, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(repaired, repair, placement,
            [new(P.PutReceipt, P.Collection, P.Hidden, P.RepairRevision)]);
        state.HiddenRevision = P.RepairRevision;
        state.RootOriginals.Add((repair, repaired));
        await RowRevisionInferenceRf3Current.CaptureOutboxAsync(root, state, P.RepairRecords, token);
        await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
        await FreshAsync(root, reader, state, kind, token);
        await RowRevisionInferenceRf3Attempts.ReplayAsync(root, reader, state, token);
        await RowRevisionInferenceRf3Current.RootReplayAsync(root, state, token);
        await ReadsAsync(root, reader, state, token);
    }

    private static async Task FreshAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, string kind, CancellationToken token)
    {
        var command = RowRevisionInferenceRf3Requests.Create(state.Partition, kind, P.Hidden, P.RepairRevision);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(P.PlacementVersion, state.Partition), token));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, placement,
            [new(RowRevisionInferenceRf3Requests.ReceiptKind(kind), P.Collection, P.Hidden, P.HealthyRevision)]);
        state.HiddenRevision = P.HealthyRevision;
        state.HiddenDeleted = kind == P.Delete;
        state.HiddenJson = kind == P.Patch ? P.PatchedJson : P.Changed;
        state.HealthyOriginal = (command, receipt);
        await RowRevisionInferenceRf3Current.CaptureOutboxAsync(root, state, P.HealthyRecords, token);
        await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
        await ReplayAsync(root, reader, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, CancellationToken token)
    {
        var item = state.HealthyOriginal;
        await SqlRf3Protocol.EqualAsync(item.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, item.Command, token))).Value);
        await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
        await SqlRf3Protocol.EqualAsync(item.Receipt, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(item.Command, token)));
        await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
    }

    internal static async Task ReadsAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RowRevisionInferenceRf3State state, CancellationToken token)
    {
        foreach (var id in new[] { P.Hidden, P.Own })
        {
            var reference = new EntityRef(state.Partition, P.Collection, id);
            DocumentReadResult expected = new(id == P.Hidden && state.HiddenDeleted ? null
                : new(reference, id == P.Hidden ? P.HealthyRevision : P.OriginalRevision,
                    id == P.Hidden ? state.HiddenJson : P.Original, false, []));
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.GetAsync(reference, token)));
            await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
            await RowRevisionInferenceRf3Current.RequireAsync(root, state, token);
        }
    }
}
