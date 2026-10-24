using System.Text.Json;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleChunkCorrectionBoundReceipts
{
    private const long CommitIncrement = 1;

    internal static async Task CommitAsync(SampleChunkCanonicalFixture fixture, Mutation mutation,
        string kind, long revision, List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition, [mutation]);
        var operation = fixture.Owner.Database.CreateCommand(OperationKind.Batch, command.CommandId, SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var authority = fixture.Owner.Store.Identity;
        var position = fixture.Owner.Database.LastApplied;
        var physicalPosition = fixture.Owner.Store.Position;
        var expected = new CommitReceipt(command.CommandId, new(authority.Incarnation, command.Partition.AtomicPartitionId, checked(position + CommitIncrement)), [new(kind, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, revision)], authority.Durability, (authority.Durability == DurabilityProfile.Buffered ? 0 : 1));
        var result = fixture.Owner.Database.ApplyEmbedded(operation, cancellationToken: default);
        originals.Add((operation, result));
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(checked(physicalPosition + CommitIncrement));
        await Assert.That(fixture.Owner.Database.LastApplied).IsEqualTo(expected.Token.Position);
        await Assert.That(JsonDefaults.Serialize(result.Get<CommitReceipt>()).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
