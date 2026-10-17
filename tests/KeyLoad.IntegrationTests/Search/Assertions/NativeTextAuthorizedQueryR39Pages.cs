using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextAuthorizedQueryR39Pages
{
    internal static async Task ReaderAsync(RequestCqrsRf3Callers reader, NativeTextAuthorizedQueryR39State state,
        long epoch, RequestCqrsRf3Callers admin, OutboxStatus baseline, CancellationToken token)
    {
        var ukrainian = epoch is 1L or 5L;
        var redacted = epoch == 3L;
        var id = ukrainian ? NativeTextMaintenanceRf3Scenario.Ukrainian : NativeTextMaintenanceRf3Scenario.English;
        var json = redacted ? "{\"owner\":\"authorized-text-owner-b\"}" : ukrainian
            ? NativeTextAuthorizedQueryR39Seed.UkrainianJson : NativeTextAuthorizedQueryR39Seed.EnglishJson;
        RankedDocument[] expected = [new(new(new(state.Original.Scenario.Partition,
            NativeTextMaintenanceRf3Scenario.Collection, id), 1L, json, redacted,
            redacted ? [NativeTextMaintenanceRf3Scenario.Field] : []), 1d / 61d, null)];
        Task CurrentAsync() => AdminAsync(admin, state, baseline, token);
        await BothAsync(reader, state, "needle", expected, token, CurrentAsync);
        await BothAsync(reader, state, "ПРИВІТ", ukrainian ? expected : [], token, CurrentAsync);
        await BothAsync(reader, state, "hello", ukrainian ? [] : expected, token, CurrentAsync);
    }

    internal static async Task AdminAsync(RequestCqrsRf3Callers admin, NativeTextAuthorizedQueryR39State state,
        OutboxStatus baseline, CancellationToken token)
    {
        var partition = state.Original.Scenario.Partition;
        Task CutAsync() => NativeTextSelectedSchemaPublicAssertions.OutboxAsync(admin, partition, baseline, token);
        var english = new DocumentResult(new(partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.English), 1L, NativeTextAuthorizedQueryR39Seed.EnglishJson, false, []);
        var ukrainian = new DocumentResult(new(partition, NativeTextMaintenanceRf3Scenario.Collection,
            NativeTextMaintenanceRf3Scenario.Ukrainian), 1L, NativeTextAuthorizedQueryR39Seed.UkrainianJson, false, []);
        await BothAsync(admin, state, "needle", [new(english, 1d / 61d, null), new(ukrainian, 1d / 62d, null)], token, CutAsync);
        await BothAsync(admin, state, "hello", [new(english, 1d / 61d, null)], token, CutAsync);
        await BothAsync(admin, state, "ПРИВІТ", [new(ukrainian, 1d / 61d, null)], token, CutAsync);
        await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(admin, partition, baseline, token);
    }

    private static async Task BothAsync(RequestCqrsRf3Callers caller, NativeTextAuthorizedQueryR39State state,
        string text, RankedDocument[] expected, CancellationToken token, Func<Task>? current = null)
    {
        foreach (var selected in new[] { true, false })
        {
            var request = new SearchRequest(state.Original.Scenario.Partition,
                NativeTextMaintenanceRf3Scenario.Collection, NativeTextMaintenanceRf3Scenario.Field, text,
                TextIndex: selected ? new(state.Original.Build.Consumer, state.Original.Build.IndexGeneration) : null);
            foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
            {
                await SqlRf3Protocol.EqualAsync(expected, await NativeTextSelectedRf3Call.ExecuteAsync(
                    caller.Sdk, caller.Mcp, request, path, token));
                if (current is not null)
                {
                    await current();
                }
            }
        }
    }
}
