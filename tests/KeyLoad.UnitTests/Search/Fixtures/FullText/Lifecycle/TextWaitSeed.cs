using System.Collections.Immutable;
using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Commits through the actual canonical engine and verifies its persisted outcome.</summary>
internal static class TextWaitSeed
{
    private const string Root = "root";
    private const long FirstIndex = 1;

    internal static async Task<CommitReceipt> CommitAsync(CanonicalReadCutFixture fixture,
        CancellationToken cancellationToken)
    {
        var database = fixture.Canonical;
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, database.Partition,
            [new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId,
                TextBilingualAudit.UkrainianJson), new PutDocument(TextBilingualAudit.Collection,
                TextBilingualAudit.EnglishId, TextBilingualAudit.EnglishJson)]);
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
            [new("putDocument", TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId, FirstIndex),
                new("putDocument", TextBilingualAudit.Collection, TextBilingualAudit.EnglishId, FirstIndex)];
        await Assert.That(receipt.Mutations).IsEquivalentTo(expected, CollectionOrdering.Matching);
        var replay = database.Database.ResolveOutcome(operation).Get<CommitReceipt>();
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        return receipt;
    }
}
