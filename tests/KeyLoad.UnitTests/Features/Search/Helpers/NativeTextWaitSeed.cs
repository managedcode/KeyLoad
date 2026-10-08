using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Replication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Seeds a real persisted log/materializer prefix for unit component checks; does not claim RF3 authority.</summary>
internal static class NativeTextWaitSeed
{
    private const string Root = "root";
    private const long InitialTerm = 1;
    private const long FirstIndex = 1;

    internal static async Task<CommitReceipt> CommitAsync(ReplicaAppliedPositionWaitFixture fixture,
        CancellationToken cancellationToken)
    {
        var database = fixture.Canonical;
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, database.Partition,
            [new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                NativeTextBilingualAudit.UkrainianJson), new PutDocument(NativeTextBilingualAudit.Collection,
                NativeTextBilingualAudit.EnglishId, NativeTextBilingualAudit.EnglishJson)]);
        var operation = database.Database.NormalizeOperation(new ReplicatedOperation(commandId, OperationKind.Batch,
            Root, database.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options)));
        fixture.Log.SaveTermAndVote(InitialTerm, fixture.Configuration.LocalId);
        fixture.Log.Append([new ReplicaEntry(FirstIndex, InitialTerm, operation)]);
        fixture.Materializer.Commit(FirstIndex);
        await fixture.Materializer.WaitForApplyAsync(FirstIndex, cancellationToken).ConfigureAwait(false);
        var receipt = database.Database.ResolveOutcome(operation).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(FirstIndex);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        ImmutableArray<MutationReceipt> expected =
            [new("putDocument", NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId, FirstIndex),
                new("putDocument", NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId, FirstIndex)];
        await Assert.That(receipt.Mutations).IsEquivalentTo(expected, CollectionOrdering.Matching);
        var replay = database.Database.ResolveOutcome(operation).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        return receipt;
    }
}
