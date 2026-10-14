using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertOwnershipEpochFlow
{
    internal static async Task RunAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
    {
        await SqlTypedInsertRetiredAssertions.ReplayAsync(source, target, state, token);
        await SqlTypedInsertOwnershipEpochRefusals.RunAsync(source, target, state, token);
        await ColdAsync(source, target, state, token);
        await SqlTypedInsertRetiredResourcePolicy.RunAsync(source, target, state, token);
        state = await SqlTypedInsertOwnershipEpochPolicy.RunAsync(source, target, state, token);
        await ReplayAsync(source, target, state, token);
        await ColdAsync(source, target, state, token);
    }
    private static Task ReplayAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
        => SqlTypedInsertRetiredImage.ObserveAsync(source, target, async () =>
    {
        await SqlTypedInsertRetiredAssertions.ReplayAsync(source, target, state, token);
            var native = SqlTypedInsertOwnershipEpochCommands.Compile(target, SqlTypedInsertRetiredLiteral.HealthyId,
                SqlTypedInsertOwnershipEpochLiteral.PublishedEpoch, native: true, token);
            await SqlTypedInsertAssertions.SameAsync(state.HealthyReceipt, target.Journal.Submit(native, token).Get<CommitReceipt>());
            await SqlTypedInsertRetiredAssertions.RowsAsync(target, repaired: true, token);
    }, () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
    private static Task ColdAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
        => SqlTypedInsertRetiredImage.ObserveAsync(source, target, async () =>
    {
        source.Reopen();
            target.Reopen();
            if (state.Healthy is not null)
        {
            await ReplayAsync(source, target, state, token);
        }
        else
        {
            await SqlTypedInsertRetiredAssertions.ReplayAsync(source, target, state, token);
                await SqlTypedInsertRetiredAssertions.RowsAsync(target, repaired: false, token);
        }
    }, () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
}

