using System.Collections.Immutable;
using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Commits through the actual canonical engine and verifies its persisted outcome.</summary>
internal static class NativeTextWaitSeed
{
    private const string Root = "root";
    private const long FirstIndex = 1;

    internal static async Task<CommitReceipt> CommitAsync(CanonicalReadCutFixture fixture,
        CancellationToken cancellationToken)
    {
        var database = fixture.Canonical;
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, database.Partition,
            [new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                NativeTextBilingualAudit.UkrainianJson), new PutDocument(NativeTextBilingualAudit.Collection,
                NativeTextBilingualAudit.EnglishId, NativeTextBilingualAudit.EnglishJson)]);
        var operation = database.Database.OwnCommand(database.Database.CreateCommand(OperationKind.Batch, commandId, Root, database.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options)));
        cancellationToken.ThrowIfCancellationRequested();
        var expectedPosition = checked(database.Database.LastApplied + 1);
        var receipt = database.SubmitIssuedEmbedded(operation, cancellationToken).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(expectedPosition);
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
