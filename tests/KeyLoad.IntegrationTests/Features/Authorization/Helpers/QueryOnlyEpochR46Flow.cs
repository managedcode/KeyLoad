using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class QueryOnlyEpochR46Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, PolicyEpochPublicState original, CancellationToken token)
    {
        var state = await RunOwnedAsync(fixture, original.CurrentReader, async (root, reader) =>
        {
            var baseline = original.CurrentReader.Principal;
            var initial = new QueryOnlyEpochR46State(original, baseline, [],
                await QueryOnlyEpochR46Current.OutboxAsync(root, original.Scenario, token), original.CurrentReader);
            await QueryOnlyEpochR46Current.RequireAsync(root, reader, initial, token);
            initial = initial with { GrantedPages = await QueryOnlyEpochR46Pages.RequireAsync(root, reader, initial, token) };
            initial = initial with
            {
                CurrentReader = await QueryOnlyEpochR46Policy.ChangeAsync(
                root.Sdk, initial.CurrentReader, baseline, false, token)
            };
            await QueryOnlyEpochR46Current.RequireAsync(root, reader, initial, token);
            await QueryOnlyEpochR46Refusals.RequireAsync(root, reader, initial, false, token);
            await QueryOnlyEpochR46Replay.RequireAsync(root, reader, initial, token);
            return initial;
        }, token);
        await QueryOnlyEpochR46Cold.RestartAsync(fixture, token);
        state = await RunOwnedAsync(fixture, state.CurrentReader, async (root, reader) =>
        {
            await QueryOnlyEpochR46Refusals.RequireAsync(root, reader, state, false, token);
            await QueryOnlyEpochR46Replay.RequireAsync(root, reader, state, token);
            var repaired = state with
            {
                CurrentReader = await QueryOnlyEpochR46Policy.ChangeAsync(
                root.Sdk, state.CurrentReader, state.Baseline, true, token)
            };
            await QueryOnlyEpochR46Current.RequireAsync(root, reader, repaired, token);
            await QueryOnlyEpochR46Refusals.RequireAsync(root, reader, repaired, true, token);
            _ = await QueryOnlyEpochR46Pages.RequireAsync(root, reader, repaired, token);
            await QueryOnlyEpochR46Replay.RequireAsync(root, reader, repaired, token);
            return repaired;
        }, token);
        await QueryOnlyEpochR46Cold.RestartAsync(fixture, token);
        _ = await RunOwnedAsync(fixture, state.CurrentReader, async (root, reader) =>
        {
            await QueryOnlyEpochR46Refusals.RequireAsync(root, reader, state, true, token);
            _ = await QueryOnlyEpochR46Pages.RequireAsync(root, reader, state, token);
            await QueryOnlyEpochR46Replay.RequireAsync(root, reader, state, token);
            return true;
        }, token);
    }

    private static Task<T> RunOwnedAsync<T>(ClusterFixture fixture, McpPersistedIdentity identity,
        Func<RequestCqrsRf3Callers, RequestCqrsRf3Callers, Task<T>> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            root => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret,
                reader => operation(root, reader), token), token);
}
