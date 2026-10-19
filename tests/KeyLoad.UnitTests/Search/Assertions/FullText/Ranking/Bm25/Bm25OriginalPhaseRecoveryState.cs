using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Search;

internal sealed record Bm25OriginalPhaseRecoveryState(Guid NodeId, Guid Incarnation, long Position,
    long Applied, byte[] Rows)
{
    internal static (CommitToken Token, long Applied) BeforeLocalPublish(DatabaseEngine database, PartitionRef partition)
        => database.Store.Read(view =>
        {
            var applied = view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
                ? OrleansSerialization.Deserialize<long>(bytes) : 0L;
            return (new CommitToken(database.Store.Identity.Incarnation, partition.AtomicPartitionId,
                checked(database.Store.Position + 1L)), applied);
        });

    internal static Bm25OriginalPhaseRecoveryState Read(Bm25OriginalPhaseRecoveryOwner owner)
    {
        var work = owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        var image = owner.Store.Read(view =>
        {
            var rows = new List<string>();
            var scan = view.VisitRange([], owner.Source.Database.Limits.MaxScanRecords,
                (key, value) =>
                {
                    rows.Add(Convert.ToHexString(key) + ":" + Convert.ToHexString(value));
                    return true;
                }, observer: work.ChargeBytes, cancellationToken: Bm25OriginalPhaseRecoveryFlow.Token);
            if (scan.HasMore)
            {
                throw new InvalidOperationException("The complete original canonical image exceeded the execution record bound.");
            }
            return (Position: owner.Store.Position, Rows: OrleansSerialization.Serialize(rows.ToArray()));
        });
        return new(owner.Store.Identity.NodeId, owner.Store.Identity.Incarnation, image.Position,
            owner.Database.LastApplied, image.Rows);
    }

    internal async Task RequireAsync(Bm25OriginalPhaseRecoveryState actual)
    {
        await Assert.That(actual.NodeId).IsEqualTo(NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(Incarnation);
        await Assert.That(actual.Position).IsEqualTo(Position);
        await Assert.That(actual.Applied).IsEqualTo(Applied);
        await ReplayResultAssertions.BytesAsync(Rows, actual.Rows);
    }

    internal static async Task RequireRetainedAsync(Bm25OriginalPhaseRecoveryOwner owner,
        Command<DatabaseCommand> original, byte[] originalEntry)
    {
        var actual = owner.PhaseEntry(original);
        await Assert.That(actual.PreparedCommand).IsNotNull();
        await Assert.That(actual.PreparedCommand!.Value!.Kind).IsEqualTo(OperationKind.Bm25CorpusPublicationPhase);
        await Assert.That(actual.PreparedCommand.CommandId).IsEqualTo(original.CommandId);
        await ReplayResultAssertions.BytesAsync(original.Value!.Input.ToArray(),
            actual.PreparedCommand.Value!.Input.ToArray());
        await ReplayResultAssertions.BytesAsync(originalEntry, OrleansSerialization.Serialize(actual));
    }
}
