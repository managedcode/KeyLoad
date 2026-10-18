using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Options;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeReplay
{
    private const string Missing = "The original committed native command outcome is not retained.";

    internal static byte[] ReadBytes(RequestCqrsHostedRuntimeFixture source,
        CancellationToken token)
    {
        var work = new ReadExecutionBudget(Options.Create(source.Database.Database.Limits), source.Clock, token);
        work.Check();
        byte[]? bytes = null;
        source.Database.Store.Read(view => view.ReadValue(KeySpace.PartitionOutcome(P.Partition, P.Root, P.Command),
            value => bytes = value.ToArray(), work.ChargeBytes));
        work.Check();
        return bytes ?? throw new InvalidOperationException(Missing);
    }

    internal static async Task RequireOriginalAsync(RequestCqrsHostedRuntimeFixture source,
        PartitionStorageNativeCommit original, CancellationToken token)
    {
        await source.Database.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var database = source.Database.Database;
        var request = database.CreateCommand(OperationKind.Batch, P.Command, P.Root,
            source.Clock.GetUtcNow(), NativeSerialization.Serialize(P.Request()));
        var retained = database.ReadStoredCommand(request, token) ?? throw new InvalidOperationException(Missing);
        await Assert.That(database.LastApplied).IsGreaterThanOrEqualTo(original.Receipt.Token.Position);
        await Assert.That(retained.Sequence).IsEqualTo(original.Receipt.Token.Position);
        await Assert.That(retained.Sequence).IsEqualTo(original.Outcome.CanonicalSequence);
        await Assert.That(retained.Command.CommandId).IsEqualTo(P.Command);
        await Assert.That(retained.Command.UserId).IsEqualTo(P.Root);
        await Assert.That(retained.Command.Timestamp).IsEqualTo(original.Outcome.PreparedCommand.Timestamp);
        await Assert.That(retained.Command.Value!.Kind).IsEqualTo(OperationKind.Batch);
        var command = NativeSerialization.Deserialize<CommandRequest>(retained.Command.Value!.Input.Span);
        await Assert.That(JsonDefaults.Serialize(command).AsSpan().SequenceEqual(JsonDefaults.Serialize(P.Request())))
            .IsTrue();
        await Assert.That(ReadBytes(source, token).AsSpan().SequenceEqual(original.Bytes)).IsTrue();
    }

    internal static async Task RequireUnchangedCutAsync(RequestCqrsHostedRuntimeFixture source,
        (long AppliedPosition, long StorePosition) expected, CancellationToken token)
    {
        await source.Database.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        await Assert.That(source.Database.ReadCanonicalCut()).IsEqualTo(expected);
    }
}
