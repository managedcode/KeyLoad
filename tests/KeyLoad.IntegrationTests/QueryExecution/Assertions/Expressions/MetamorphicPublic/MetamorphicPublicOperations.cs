using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class MetamorphicPublicOperations
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader, MetamorphicPublicState state,
        MetamorphicPublicPlan[] plans, bool descending, int offset, bool denied, CancellationToken token)
    {
        var expected = MetamorphicPublicData.Reference(state.Rows, descending, offset);
        await Assert.That(expected.Length).IsGreaterThan(MetamorphicPublicData.PageSize);
        for (var shape = 0; shape < plans.Length; shape++)
        {
            for (var route = 0; route < 4; route++)
            {
                if (denied)
                {
                    await MetamorphicPublicCurrent.ObserveAsync(root, state, () => MetamorphicPublicCalls.DeniedAsync(
                        reader, plans[shape], route, state.Identity.Secret, token), token);
                    continue;
                }
                foreach (var official in new[] { false, true })
                {
                    await PagesAsync(root, reader, state, plans[shape], route, MetamorphicPublicData.Path(shape),
                        expected, official, token);
                }
            }
        }
    }
    private static async Task PagesAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader, MetamorphicPublicState state,
        MetamorphicPublicPlan plan, int route, string path, ImmutableArray<MetamorphicPublicRow> expected,
        bool official, CancellationToken token)
    {
        var seen = 0;
        string? cursor = null;
        long? cut = null;
        do
        {
            await MetamorphicPublicCurrent.ObserveAsync(root, state, async () =>
            {
                var page = official ? (await McpCallerAssertions.SuccessAsync<QueryPage>(
                    await MetamorphicPublicCalls.McpAsync(reader, plan, route, cursor, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await MetamorphicPublicCalls.SdkAsync(reader, plan, route, cursor, token));
                var rows = expected.Skip(seen).Take(MetamorphicPublicData.PageSize).Select(MetamorphicPublicData.Expected).ToArray();
                await MetamorphicPublicResults.PageAsync(page, rows, path, seen + rows.Length == expected.Length,
                    cut);
                await Assert.That(page.Rows.Any(row => row.Json.Contains("PRIVATE-54", StringComparison.Ordinal))).IsFalse();
                seen += rows.Length;
                cut ??= page.CutPosition;
                cursor = page.Cursor;
            }, token);
        } while (cursor is not null);
        await Assert.That(seen).IsEqualTo(expected.Length);
    }
}
