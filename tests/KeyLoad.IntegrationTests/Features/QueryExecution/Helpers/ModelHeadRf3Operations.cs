using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadRf3Operations
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader, ModelHeadRf3State state,
        bool denied, CancellationToken token)
    {
        foreach (var queue in new[] { false, true })
        {
            if (denied)
            {
                await ModelHeadRf3Current.ObserveAsync(root, state, () => ModelHeadRf3Refusals.ReadAsync(reader,
                    state, queue, 1L, ErrorCode.PermissionDenied, "The principal cannot perform this operation in this scope.", token), token);
                continue;
            }
            await ModelHeadRf3Current.ObserveAsync(root, state,
                () => ModelHeadRf3Typed.RunAsync(reader, state, queue, token), token);
            for (var route = 0; route < 6; route++)
            {
                await ModelHeadRf3Current.ObserveAsync(root, state, async () => await PageAsync(
                    await ModelHeadRf3Calls.ReadAsync(reader, state, queue, route, token), queue), token);
            }
        }
        if (!denied)
        {
            await ModelHeadRf3Current.ObserveAsync(root, state, () => ModelHeadRf3Refusals.ReadAsync(reader, state,
                false, 2L, ErrorCode.TokenInvalidated, "The event source generation is stale.", token), token);
            await ModelHeadRf3Current.ObserveAsync(root, state, () => ModelHeadRf3Refusals.UpdateAsync(reader, state, token), token);
            await ModelHeadRf3Current.ObserveAsync(root, state, async () => await PageAsync(
                await ModelHeadRf3Calls.ReadAsync(reader, state, false, 0, token), false), token);
            await ModelHeadRf3Current.ReplayAsync(root, state, token);
        }
    }
    private static async Task PageAsync(QueryPage page, bool queue)
    {
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.AccessPath).IsEqualTo(ModelHeadRf3Requests.Path(queue));
        await Assert.That(page.Cursor).IsNull();
        var expected = ModelHeadRf3Requests.Rows(queue);
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Length);
        foreach (var (row, golden) in page.Rows.Zip(expected))
        {
            using var actual = JsonDocument.Parse(row.Json);
            using var value = JsonDocument.Parse(golden.Json);
            await Assert.That(JsonElement.DeepEquals(actual.RootElement, value.RootElement)).IsTrue();
            await SqlRf3Protocol.EqualAsync(golden with
            {
                Json = ""
            }, row with
            {
                Json = ""
            });
            await Assert.That(row.Json.Contains(ModelHeadRf3State.Secret, StringComparison.Ordinal)).IsFalse();
        }
    }
}
