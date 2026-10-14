using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertOwnershipEpochRefusals
{
    internal static async Task RunAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, CancellationToken token)
    {
        await InvalidAsync(source, target, state, SqlTypedInsertOwnershipEpochLiteral.ZeroEpoch, token);
        await InvalidAsync(source, target, state, SqlTypedInsertOwnershipEpochLiteral.NegativeEpoch, token);
        await StaleAsync(source, target, state, SqlTypedInsertOwnershipEpochLiteral.StaleId,
            SqlTypedInsertOwnershipEpochLiteral.StaleEpoch, token);
        await StaleAsync(source, target, state, SqlTypedInsertOwnershipEpochLiteral.FutureId,
            SqlTypedInsertOwnershipEpochLiteral.FutureEpoch, token);
    }
    private static Task InvalidAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, long epoch, CancellationToken token)
        => SqlTypedInsertRetiredImage.ObserveAsync(source, target, async () =>
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => SqlTypedInsertOwnershipEpochCommands.Compile(target,
                SqlTypedInsertRetiredLiteral.HealthyId, epoch, native: false, token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        error = Assert.ThrowsExactly<KeyLoadException>(() => SqlTypedInsertOwnershipEpochCommands.Compile(target,
            SqlTypedInsertRetiredLiteral.HealthyId, epoch, native: true, token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }, () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
    private static async Task StaleAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, Guid id, long epoch, CancellationToken token)
    {
        var original = SqlTypedInsertOwnershipEpochCommands.Compile(target, id, epoch, native: false, token);
        var expected = new OperationResult(null, ErrorCode.OwnershipLost, SqlTypedInsertOwnershipEpochLiteral.Stale);
        await FirstAsync(source, target, state, original, expected, token);
        // First admitted failure may retain its native failed outcome/clock; replay uses the complete post-failure baseline.
        await SqlTypedInsertRetiredImage.ObserveAsync(source, target, async () =>
        {
            await SqlTypedInsertAssertions.SameAsync(expected, target.Journal.Submit(original, token));
            var native = SqlTypedInsertOwnershipEpochCommands.Compile(target, id, epoch, native: true, token);
            await SqlTypedInsertAssertions.SameAsync(expected, target.Journal.Submit(native, token));
        }, () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
    }
    private static async Task FirstAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        SqlTypedInsertRetiredState state, ReplicatedOperation original, OperationResult expected, CancellationToken token)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var before = target.Database.GetOutboxStatus(PhysicalShardCatalogFixture.RootPrincipalId, partition);
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var sourceCut = source.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
            await SqlTypedInsertAssertions.SameAsync(expected, target.Journal.Submit(original, token)), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlTypedInsertAssertions.SameAsync(before,
            target.Database.GetOutboxStatus(PhysicalShardCatalogFixture.RootPrincipalId, partition)), failures);
        await ServerFailureObserver.ObserveAsync(() => SqlTypedInsertRetiredAssertions.RowsAsync(target, repaired: false, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SqlTypedInsertAssertions.UnchangedAsync(source.Store, sourceImage, sourceCut), failures);
        await ServerFailureObserver.ObserveAsync(() => state.Authority.RemainsControlOwnedAsync(source, target), failures);
        await ServerFailureObserver.ObserveAsync(() => state.FailedAuthority.RemainsControlOwnedAsync(source, target), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

