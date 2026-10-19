using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class EventProjectionLineageR41Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var state = await EventProjectionLineageR41Seed.CreateAsync(fixture, admin, token);
            await WithActorsAsync(fixture, state, async (worker, reader) =>
      {
          await EventProjectionLineageR41Operations.ApplyAsync(worker, state, token);
          await EventProjectionLineageR41Canonical.CaptureOutboxAsync(admin, state, token);
          await EventProjectionLineageR41Canonical.CurrentAsync(admin, reader, state, token);
          await EventProjectionLineageR41Replay.ProjectionAsync(admin, worker, reader, state, token);
          await EventProjectionLineageR41Operations.DeleteAsync(admin, state, token);
          await EventProjectionLineageR41Canonical.CaptureOutboxAsync(admin, state, token);
          await EventProjectionLineageR41Refusal.BothAsync(admin, worker, reader, state, token);
          await EventProjectionLineageR41Operations.ReplaceAsync(admin, state, token);
          await EventProjectionLineageR41Canonical.CaptureOutboxAsync(admin, state, token);
          await RetainedAsync(admin, worker, reader, state, token);
      }, token);
            return state;
        }, token);
        for (var cold = 0; cold < 2; cold++)
        {
            await TextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
            {
                await WithActorsAsync(fixture, state, (worker, reader) => RetainedAsync(admin, worker, reader, state, token), token);
                return true;
            }, token);
        }
    }
    private static Task<bool> WithActorsAsync(ClusterFixture fixture, EventProjectionLineageR41State state,
     Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> body, CancellationToken token)
     => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Worker.Secret,
      worker => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, state.Reader.Secret,
       async reader => { await body(worker, reader); return true; }, token), token);
    private static async Task RetainedAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
     RequestCqrsClusterCallers reader, EventProjectionLineageR41State state, CancellationToken token)
    {
        await EventProjectionLineageR41Canonical.CurrentAsync(admin, reader, state, token);
        await EventProjectionLineageR41Refusal.BothAsync(admin, worker, reader, state, token);
        await EventProjectionLineageR41Replay.SuccessesAsync(admin, reader, state, token);
    }
}
