using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeReplay
{
    private const int NoAdmissions = 0;
    private const int SingleAdmission = 1;
    private const long NextIndex = 1;
    private const string Missing = "The original native storage entry is not retained.";

    internal static byte[] ReadBytes(ControlledPartitionMovementNativeHost source, long index,
        CancellationToken token)
    {
        var work = new ReadExecutionBudget(source.Runtime.Core.DatabaseLimits, source.Clock, token);
        byte[]? bytes = null;
        source.Store.Read(view => view.ReadValue(ReplicaProtocol.EntryStorageKey(index),
            value => bytes = value.ToArray(), work.ChargeBytes));
        work.Check();
        return bytes ?? throw new InvalidOperationException(Missing);
    }

    internal static async Task RequireOriginalAsync(ControlledPartitionMovementNativeHost source,
        PartitionStorageNativeCommit original, CancellationToken token)
    {
        await source.Partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var work = new ReadExecutionBudget(source.Runtime.Core.DatabaseLimits, source.Clock, token);
        await Assert.That(source.Log.State.CommittedIndex >= original.Receipt.Token.Position).IsTrue();
        var entry = source.Log.ReadEntry(original.Receipt.Token.Position, work.ChargeBytes)
            ?? throw new InvalidOperationException(Missing);
        work.Check();
        await Assert.That(entry.Index).IsEqualTo(original.Entry.Index);
        await Assert.That(entry.Term).IsEqualTo(original.Entry.Term);
        await Assert.That(entry.Operation is not null).IsTrue();
        await Assert.That(source.Database.NativeOperationsEqual(original.Entry.Operation!, entry.Operation!)).IsTrue();
        await Assert.That(ReadBytes(source, entry.Index, token).AsSpan().SequenceEqual(original.Bytes)).IsTrue();
    }

    internal static async Task RequireAdmissionAsync(ControlledPartitionMovementNativeHost source,
        long committedBefore, CancellationToken token)
    {
        var work = new ReadExecutionBudget(source.Runtime.Core.DatabaseLimits, source.Clock, token);
        var committedAfter = source.Log.State.CommittedIndex;
        var examined = NoAdmissions;
        var admissions = NoAdmissions;
        for (var index = checked(committedBefore + NextIndex); index <= committedAfter; index++)
        {
            work.Check();
            if (examined >= source.Database.Limits.MaxScanRecords)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The native storage retry tail exhausted its record bound.");
            }
            examined++;
            var entry = source.Log.ReadEntry(index, work.ChargeBytes)
                ?? throw new InvalidOperationException("The committed native storage retry tail is incomplete.");
            if (entry.Operation?.Id != P.Command)
            {
                continue;
            }
            admissions++;
            await Assert.That(entry.Operation.Value!.Kind).IsEqualTo(OperationKind.Batch);
            await Assert.That(entry.Operation.UserId!).IsEqualTo(P.Root);
            var command = NativeSerialization.Deserialize<CommandRequest>((entry.Operation).Value!.Input.Span);
            await Assert.That(JsonDefaults.Serialize(command).AsSpan().SequenceEqual(JsonDefaults.Serialize(P.Request())))
                .IsTrue();
        }
        work.Check();
        await Assert.That(admissions).IsEqualTo(SingleAdmission);
    }
}
