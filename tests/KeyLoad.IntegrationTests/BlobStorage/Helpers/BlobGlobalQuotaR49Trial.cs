using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

using KeyLoad.IntegrationTests.Features.Search;
internal static class BlobGlobalQuotaR49Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            var state = await BlobGlobalQuotaR49Seed.RunAsync(callers, token);
            await BlobGlobalQuotaR49Operations.BeginAsync(callers, state, state.Held, state.Held.Begin, token);
            await BlobGlobalQuotaR49Operations.PartAsync(callers, state, state.Held, 0, BlobGlobalQuotaR49Corpus.PartLength, token);
            await BlobGlobalQuotaR49Replay.RefusedAsync(callers, state, token);
            await BlobGlobalQuotaR49Replay.UploadAsync(callers, state, state.Held, token);
            await BlobGlobalQuotaR49Operations.AbortAsync(callers, state, state.Held, token);
            await BlobGlobalQuotaR49Replay.RefusedAsync(callers, state, token);
            await BlobGlobalQuotaR49Replay.UploadAsync(callers, state, state.Held, token);
            var accepted = state.Attempt.Begin with
            {
                CommandId = Guid.NewGuid()
            };
            await BlobGlobalQuotaR49Operations.BeginAsync(callers, state, state.Attempt, accepted, token);
            await BlobGlobalQuotaR49Replay.RefusedAsync(callers, state, token);
            await BlobGlobalQuotaR49Operations.AbortAsync(callers, state, state.Attempt, token);
            await BlobGlobalQuotaR49Operations.ReclaimAsync(callers, state, state.Held, 1, BlobGlobalQuotaR49Corpus.PartLength, token);
            await BlobGlobalQuotaR49Operations.ReclaimAsync(callers, state, state.Attempt, 0, 0L, token);
            await BlobGlobalQuotaR49Replay.AllAsync(callers, state, token);
            return state;
        }, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            await BlobGlobalQuotaR49Replay.AllAsync(callers, state, token);
            var healthy = new BlobGlobalQuotaR49Upload(state.Attempt.Blob, BlobGlobalQuotaR49Corpus.SmallLength);
            state.Healthy = healthy;
            await BlobGlobalQuotaR49Operations.PublishAsync(callers, state, healthy, token);
            await BlobGlobalQuotaR49Replay.AllAsync(callers, state, token);
            return true;
        }, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            await BlobGlobalQuotaR49Replay.AllAsync(callers, state, token);
            return true;
        }, token);
    }
}
