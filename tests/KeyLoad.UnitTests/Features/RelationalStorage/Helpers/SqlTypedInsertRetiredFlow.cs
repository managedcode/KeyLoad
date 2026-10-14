using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertRetiredFlow
{
    internal static async Task RunAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
    {
        await SqlTypedInsertRetiredAssertions.ReplayAsync(source, target, state, token);
        await SqlTypedInsertRetiredAssertions.RowsAsync(target, repaired: false, token);
        await ColdAsync(source, target, state, token);
        await SqlTypedInsertRetiredResourcePolicy.RunAsync(source, target, state, token);
        state = await SqlTypedInsertRetiredPolicy.RunAsync(source, target, state, token);
        await SqlTypedInsertRetiredAssertions.ReplayAsync(source, target, state, token);
        await SqlTypedInsertRetiredAssertions.RowsAsync(target, repaired: true, token);
        await ColdAsync(source, target, state, token);
    }
    private static async Task ColdAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
    {
        await SqlTypedInsertRetiredImage.ObserveAsync(source, target, async () =>
        {
            source.Reopen();
            target.Reopen();
            await SqlTypedInsertRetiredAssertions.ReplayAsync(source, target, state, token);
            await SqlTypedInsertRetiredAssertions.RowsAsync(target, state.EpochFenced, token);
        }, () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
    }
}
