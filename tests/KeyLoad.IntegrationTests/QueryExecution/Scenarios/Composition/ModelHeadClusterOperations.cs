using System.Text.Json;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadClusterOperations
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader, ModelHeadClusterState state,
        bool denied, CancellationToken token)
    {
        foreach (var queue in new[] { false, true })
        {
            if (denied)
            {
                await ModelHeadClusterCurrent.ObserveAsync(root, state, () => ModelHeadClusterRefusals.ReadAsync(reader,
                    state, queue, 1L, ErrorCode.PermissionDenied, "The principal cannot perform this operation in this scope.", token), token);
                continue;
            }
            await ModelHeadClusterCurrent.ObserveAsync(root, state,
                () => ModelHeadClusterTyped.RunAsync(reader, state, queue, token), token);
            for (var route = 0; route < 6; route++)
            {
                await ModelHeadClusterCurrent.ObserveAsync(root, state, async () => await PageAsync(
                    await ModelHeadClusterCalls.ReadAsync(reader, state, queue, route, token), queue), token);
            }
        }
        if (!denied)
        {
            await ModelHeadClusterCurrent.ObserveAsync(root, state, () => ModelHeadClusterRefusals.ReadAsync(reader, state,
                false, 2L, ErrorCode.TokenInvalidated, "The event source generation is stale.", token), token);
            await ModelHeadClusterCurrent.ObserveAsync(root, state, () => ModelHeadClusterRefusals.UpdateAsync(reader, state, token), token);
            await ModelHeadClusterCurrent.ObserveAsync(root, state, async () => await PageAsync(
                await ModelHeadClusterCalls.ReadAsync(reader, state, false, 0, token), false), token);
            await ModelHeadClusterCurrent.ReplayAsync(root, state, token);
        }
    }
    private static async Task PageAsync(QueryPage page, bool queue)
    {
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.AccessPath).IsEqualTo(ModelHeadClusterRequests.Path(queue));
        await Assert.That(page.Cursor).IsNull();
        var expected = ModelHeadClusterRequests.Rows(queue);
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Length);
        foreach (var (row, golden) in page.Rows.Zip(expected))
        {
            using var actual = JsonDocument.Parse(row.Json);
            using var value = JsonDocument.Parse(golden.Json);
            await Assert.That(JsonElement.DeepEquals(actual.RootElement, value.RootElement)).IsTrue();
            await SqlClusterProtocol.EqualAsync(golden with
            {
                Json = ""
            }, row with
            {
                Json = ""
            });
            await Assert.That(row.Json.Contains(ModelHeadClusterState.Secret, StringComparison.Ordinal)).IsFalse();
        }
    }
}
