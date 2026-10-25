using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.FunctionalTests.StorageRecovery.PartitionStorageProtocol;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionStorageReplay
{
    private const string Missing = "The original committed execution command outcome is not retained.";

    internal static byte[] ReadBytes(IServiceProvider source,
        CancellationToken token)
    {
        var database = source.GetRequiredService<PartitionHostApplicationOwner>().Borrow().Database;
        var work = new ReadExecutionBudget(source.GetRequiredService<TimeProvider>(), token);
        work.Check();
        byte[]? bytes = null;
        database.Store.Read(view => view.ReadValue(KeySpace.PartitionOutcome(P.Partition, P.Root, P.Command),
            value => bytes = value.ToArray(), work.ChargeBytes));
        work.Check();
        return bytes ?? throw new InvalidOperationException(Missing);
    }

    internal static async Task RequireOriginalAsync(IServiceProvider source,
        PartitionStorageCommit original, CancellationToken token)
    {
        var owner = source.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
        await owner.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var database = owner.Database;
        var request = database.CreateCommand(OperationKind.Batch, P.Command, P.Root,
            source.GetRequiredService<TimeProvider>().GetUtcNow(), OrleansSerialization.Serialize(P.Request()));
        var retained = database.ReadStoredCommand(request, token).Value ?? throw new InvalidOperationException(Missing);
        await Assert.That(database.LastApplied).IsGreaterThanOrEqualTo(original.Receipt.Token.Position);
        await Assert.That(retained.Sequence).IsEqualTo(original.Receipt.Token.Position);
        await Assert.That(retained.Sequence).IsEqualTo(original.Outcome.CanonicalSequence);
        await Assert.That(retained.Command.CommandId).IsEqualTo(P.Command);
        await Assert.That(retained.Command.UserId).IsEqualTo(P.Root);
        await Assert.That(retained.Command.Timestamp).IsEqualTo(original.Outcome.PreparedCommand.Timestamp);
        await Assert.That(retained.Command.Value!.Kind).IsEqualTo(OperationKind.Batch);
        var command = OrleansSerialization.Deserialize<CommandRequest>(retained.Command.Value!.Input.Span);
        await Assert.That(JsonDefaults.Serialize(command).AsSpan().SequenceEqual(JsonDefaults.Serialize(P.Request())))
            .IsTrue();
        await Assert.That(ReadBytes(source, token).AsSpan().SequenceEqual(original.Bytes)).IsTrue();
    }

    internal static async Task RequireUnchangedCutAsync(IServiceProvider source,
        (long AppliedPosition, long StorePosition) expected, CancellationToken token)
    {
        await source.GetRequiredService<PartitionHostApplicationOwner>().Borrow().Coordinator
            .ReadBarrierAsync(token).ConfigureAwait(false);
        await Assert.That(ReadCut(source)).IsEqualTo(expected);
    }

    internal static (long AppliedPosition, long StorePosition) ReadCut(IServiceProvider source)
    {
        var database = source.GetRequiredService<PartitionHostApplicationOwner>().Borrow().Database;
        return database.Store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? OrleansSerialization.Deserialize<long>(bytes) : default(long), database.Store.Position));
    }
}
