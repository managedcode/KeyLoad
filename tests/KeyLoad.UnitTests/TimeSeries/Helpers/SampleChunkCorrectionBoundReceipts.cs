using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkCorrectionBoundReceipts
{
    private const long OwnershipEpoch = 1;
    private const long CommitIncrement = 1;

    internal static async Task CommitAsync(SampleChunkCanonicalFixture fixture, Mutation mutation,
        string kind, long revision, List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition, [mutation]);
        var operation = new ReplicatedOperation(command.CommandId, OperationKind.Batch,
            SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var authority = fixture.Owner.Store.Identity;
        var position = fixture.Owner.Store.Position;
        var expected = new CommitReceipt(command.CommandId,
            new(authority.Incarnation, command.Partition.AtomicPartitionId, checked(position + CommitIncrement), OwnershipEpoch),
            [new(kind, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, revision)], authority.Durability);
        var result = fixture.Owner.Database.ApplyEmbedded(operation, cancellationToken: default);
        originals.Add((operation, result));
        await Assert.That(JsonDefaults.Serialize(result.Get<CommitReceipt>()).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
