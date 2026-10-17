using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.RowRevisionInferenceRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RowRevisionInferenceRf3Current
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers root, RowRevisionInferenceRf3State state, CancellationToken token)
    {
        foreach (var id in new[] { P.Hidden, P.Own })
        {
            var reference = new EntityRef(state.Partition, P.Collection, id);
            var json = id == P.Own ? P.Original : state.HiddenJson;
            var revision = id == P.Own ? P.OriginalRevision : state.HiddenRevision;
            DocumentReadResult expected = new(id == P.Hidden && state.HiddenDeleted
                ? null : new(reference, revision, json, false, []));
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.OutboxStatusAsync(state.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await root.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value);
    }

    internal static async Task CaptureOutboxAsync(RequestCqrsRf3Callers root, RowRevisionInferenceRf3State state,
        int expectedRecords, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
        await Assert.That(actual.Head.Tail).IsEqualTo((long)expectedRecords);
        await Assert.That(actual.Head.StoredRecords).IsEqualTo((long)expectedRecords);
        await Assert.That(actual.Head.FirstAvailable).IsEqualTo(P.OriginalRevision);
        await Assert.That(actual.Head.StoredBytes).IsGreaterThan((long)P.First);
        await Assert.That(actual.Consumers).IsEmpty();
        state.Outbox = actual;
    }

    internal static async Task RootReplayAsync(RequestCqrsRf3Callers root, RowRevisionInferenceRf3State state, CancellationToken token)
    {
        foreach (var item in state.RootOriginals)
        {
            await SqlRf3Protocol.EqualAsync(item.Receipt, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(item.Command, token)));
            await RequireAsync(root, state, token);
            await SqlRf3Protocol.EqualAsync(item.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, item.Command, token))).Value);
            await RequireAsync(root, state, token);
        }
    }
}
