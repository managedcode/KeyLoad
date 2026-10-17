using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TenantAdmissionCold
{
    internal static async Task RunAsync(TestDatabase fixture, ReplicatedOperation[] operations,
        List<OperationResult> results, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution,
            UnitExecutionOptions.PointCacheExecution());
        var database = fixture.CreateConfiguredDatabase(store);
        using var native = new TestDatabaseReplicaAdmission(database, store, fixture.Directory,
            UnitExecutionOptions.ReplicaExecution());
        native.ReadBarrier(token);
        await ReplayAsync(database, store, operation => native.Submit(operation, explicitTime: true, token),
            operations, results, fixture.Partition, token);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, fixture.Partition,
            [new PutDocument(TenantAdmissionOperations.Collection, "cold-healthy", TenantAdmissionOperations.Body)]);
        var fresh = database.CreateNativeOperation(OperationKind.Batch, id,
            TenantAdmissionOperations.PrincipalA, database.EvaluationClock.GetUtcNow(),
            global::KeyLoad.NativeSerialization.Serialize(request));
        native.Submit(fresh, explicitTime: true, token).Get<CommitReceipt>();
        await TenantAdmissionAssertions.RecordAsync(database, fresh, fixture.Partition, "cold-healthy");
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        Func<ReplicatedOperation, OperationResult> replay, ReplicatedOperation[] operations,
        List<OperationResult> results, PartitionRef firstPartition, CancellationToken token)
    {
        var state = TenantAdmissionAssertions.State(store);
        var position = store.Position;
        string[] ids = ["a1", "b1", "a2", "a3", "healthy-b"];
        for (var index = 0; index < operations.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            await NativeReplayResultAssertions.Same<CommitReceipt>(replay(operations[index]), results[index]);
            var partition = operations[index].PrincipalId == TenantAdmissionOperations.PrincipalA
                ? firstPartition : firstPartition with
                {
                    TenantId = TenantAdmissionOperations.OtherTenant
                };
            await TenantAdmissionAssertions.RecordAsync(database, operations[index], partition, ids[index]);
        }

        await Assert.That(TenantAdmissionAssertions.State(store).SequenceEqual(state)).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
    }
}
