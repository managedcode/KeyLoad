
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCursorRequestIdentityR47Flow
{
    internal static async Task RunAsync(ClusterFixture fixture,
        CancellationToken token)
    {
        var seeded = await QueryCursorRequestIdentityR47Seed.RunAsync(fixture, token);
        await Assert.That(seeded.CompleteRows.Length).IsEqualTo(4);
        await Assert.That(seeded.Documents.Length).IsEqualTo(8);
        var state = await QueryCursorRequestIdentityR47Callers.RunAsync(fixture, seeded.Credential, async (root, reader) =>
        {
            await QueryCursorRequestIdentityR47Current.RequireAsync(root, reader, seeded, token);
            var captured = seeded with
            {
                OriginalPages = await QueryCursorRequestIdentityR47Pages.FreshAsync(root, reader, seeded, token)
            };
            await QueryCursorRequestIdentityR47Refusals.RequireAsync(root, reader, captured, false, token);
            await QueryCursorRequestIdentityR47Replay.RequireAsync(root, reader, captured, token);
            return captured;
        }, token);
        await QueryCursorRequestIdentityR47Cold.RestartAsync(fixture, token);
        await ColdAsync(fixture, state, token);
        await QueryCursorRequestIdentityR47Cold.RestartAsync(fixture, token);
        await ColdAsync(fixture, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture,
        QueryCursorRequestIdentityR47State state, CancellationToken token)
        => _ = await QueryCursorRequestIdentityR47Callers.RunAsync(fixture, state.Credential, async (root, reader) =>
        {
            await QueryCursorRequestIdentityR47Refusals.RequireAsync(root, reader, state, true, token);
            _ = await QueryCursorRequestIdentityR47Pages.FreshAsync(root, reader, state, token);
            await QueryCursorRequestIdentityR47Replay.RequireAsync(root, reader, state, token);
            return true;
        }, token);
}
