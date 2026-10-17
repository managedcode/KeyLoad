using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleRf3Effects
{
    internal static async Task VerifyAsync(RequestCqrsRf3Callers administrator,
        DropTableLifecycleWaveState state, CancellationToken token)
    {
        foreach (var drop in state.Drops)
        {
            await DropTableLifecycleSeedAssertions.RowsAsync(administrator, drop.Fresh.Rows, token).ConfigureAwait(false);
            await DropTableLifecycleFreshReads.OldAbsentAsync(administrator, drop.Table, token).ConfigureAwait(false);
        }
        await DropTableLifecycleSeedAssertions.AfterAsync(administrator, state.Seed, token).ConfigureAwait(false);
    }
}
