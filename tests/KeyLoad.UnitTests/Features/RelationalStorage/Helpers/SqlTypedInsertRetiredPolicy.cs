using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertRetiredPolicy
{
    internal static async Task<SqlTypedInsertRetiredState> RunAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, SqlTypedInsertRetiredState state, CancellationToken token)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var denied = state.Principal with
        {
            PolicyEpoch = DeniedEpoch,
            Grants = [new(partition.DatabaseId, SqlTypedInsertRetiredLiteral.Table, Capability.DocumentsRead | Capability.Query)]
        };
        await ConfigureAsync(source, target, denied, token);
        await SqlTypedInsertRetiredImage.ObserveAsync(source, target,
            () => SqlTypedInsertRetiredAssertions.FencedAsync(source, state, SqlTypedInsertLiteral.ScopeDenied, token),
            () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
        var repaired = state.Principal with { PolicyEpoch = RepairedEpoch };
        await ConfigureAsync(source, target, repaired, token);
        await SqlTypedInsertRetiredImage.ObserveAsync(source, target,
            () => SqlTypedInsertRetiredAssertions.FencedAsync(source, state, SqlTypedInsertLiteral.EpochChanged, token),
            () => state.Authority.RemainsControlOwnedAsync(source, target),
            () => state.FailedAuthority.RemainsControlOwnedAsync(source, target));
        var command = new CommandRequest(SqlTypedInsertRetiredLiteral.HealthyId, partition,
            [new InsertTypedRow(SqlTypedInsertRetiredLiteral.Table, SqlTypedInsertRetiredLiteral.TwoJson)], SqlTypedInsertRetiredLiteral.PublishedEpoch);
        var original = target.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId, repaired.Id,
            target.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        var placement = target.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            new(SqlTypedInsertRetiredLiteral.Version, partition));
        await Assert.That(placement.PhysicalShardId).IsEqualTo(target.PhysicalOwner.PhysicalShardId);
        await Assert.That(placement.PlacementEpoch).IsEqualTo(SqlTypedInsertRetiredLiteral.PublishedEpoch);
        var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation, partition.AtomicPartitionId,
            checked(target.Journal.Log.State.LastIndex + NextEntry), SqlTypedInsertRetiredLiteral.PublishedEpoch),
            [new(SqlTypedInsertRetiredLiteral.PutKind, SqlTypedInsertRetiredLiteral.Table, SqlTypedInsertRetiredLiteral.Two,
                SqlTypedInsertRetiredLiteral.Revision)], DurabilityProfile.ProcessDurable);
        await SqlTypedInsertAssertions.SameAsync(expected, target.Journal.Submit(original, token).Get<CommitReceipt>());
        return state with { Principal = repaired, EpochFenced = true, Healthy = original, HealthyReceipt = expected };
    }
    private static async Task ConfigureAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        PrincipalRecord expected, CancellationToken token)
    {
        await SqlTypedInsertAssertions.SameAsync(expected, SqlTypedInsertRetiredSeed.Configure(source, expected, token));
        await SqlTypedInsertAssertions.SameAsync(expected, SqlTypedInsertRetiredSeed.Configure(target, expected, token));
    }
    private const long DeniedEpoch = 2;
    private const long RepairedEpoch = 3;
    private const long NextEntry = 1;
}
