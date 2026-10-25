using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using P = KeyLoad.ClusterTests.Authorization.RowRevisionInferenceClusterProtocol;

namespace KeyLoad.ClusterTests.Authorization;

internal static class RowRevisionInferenceClusterCurrent
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers root, RowRevisionInferenceClusterState state, CancellationToken token)
    {
        foreach (var id in new[] { P.Hidden, P.Own })
        {
            var reference = new EntityRef(state.Partition, P.Collection, id);
            var json = id == P.Own ? P.Original : state.HiddenJson;
            var revision = id == P.Own ? P.OriginalRevision : state.HiddenRevision;
            DocumentReadResult expected = new(id == P.Hidden && state.HiddenDeleted
                ? null : new(state.StoredReference(id), revision, json, false, []));
            await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
        await SqlClusterProtocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.OutboxStatusAsync(state.Partition, token)));
        await SqlClusterProtocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await root.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value);
    }

    internal static async Task CaptureOutboxAsync(RequestCqrsClusterCallers root, RowRevisionInferenceClusterState state,
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

    internal static async Task RootReplayAsync(RequestCqrsClusterCallers root, RowRevisionInferenceClusterState state, CancellationToken token)
    {
        foreach (var item in state.RootOriginals)
        {
            await SqlClusterProtocol.EqualAsync(item.Receipt, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(item.Command, token)));
            await RequireAsync(root, state, token);
            await SqlClusterProtocol.EqualAsync(item.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, item.Command, token))).Value);
            await RequireAsync(root, state, token);
        }
    }
}
