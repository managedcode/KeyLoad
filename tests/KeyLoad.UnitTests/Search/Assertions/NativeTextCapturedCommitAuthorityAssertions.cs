using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using P = KeyLoad.UnitTests.Features.Search.NativeTextCapturedCommitAuthorityProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextCapturedCommitAuthorityAssertions
{
    internal static async Task OldAsync(PartitionRef partition, RankedDocument[] actual)
    {
        RankedDocument[] expected =
        [
            new(NativeTextBm25ExpectedRows.Document(partition, P.Second, P.InitialRevision,
                P.SecondJson, false, []), 1d / 61d, null),
            new(NativeTextBm25ExpectedRows.Document(partition, P.First, P.InitialRevision,
                P.FirstJson, false, []), 1d / 62d, null)
        ];
        await NativeTextBm25ExpectedRows.RequireOwnershipAsync(expected, actual);
        await NativeTextBm25ExpectedRows.RequireWholeValuesAsync(expected, actual);
    }

    internal static async Task FreshAsync(PartitionRef partition, RankedDocument[] actual)
    {
        RankedDocument[] expected =
        [
            new(NativeTextBm25ExpectedRows.Document(partition, P.First, P.UpdatedRevision,
                P.UpdatedFirstJson, false, []), 1d / 61d, null),
            new(NativeTextBm25ExpectedRows.Document(partition, P.Third, P.InitialRevision,
                P.SecondJson, false, []), 1d / 62d, null)
        ];
        await NativeTextBm25ExpectedRows.RequireOwnershipAsync(expected, actual);
        await NativeTextBm25ExpectedRows.RequireWholeValuesAsync(expected, actual);
    }

    internal static async Task SeedReceiptAsync(PartitionRef partition, CommandRequest command, CommitReceipt actual)
    {
        var expected = new CommitReceipt(command.CommandId, actual.Token,
        [
            new(P.PutKind, P.Collection, P.First, P.InitialRevision),
            new(P.PutKind, P.Collection, P.Second, P.InitialRevision)
        ], DurabilityProfile.ProcessDurable);
        await SafeProjectorOperationSeed.EqualAsync(actual, expected);
        await TokenAsync(partition, actual.Token);
    }

    internal static async Task ReceiptAsync(PartitionRef partition, CommandRequest command, CommitReceipt actual)
    {
        var expected = new CommitReceipt(command.CommandId, actual.Token,
        [
            new(P.PutKind, P.Collection, P.First, P.UpdatedRevision),
            new(P.DeleteKind, P.Collection, P.Second, P.UpdatedRevision),
            new(P.PutKind, P.Collection, P.Third, P.InitialRevision)
        ], DurabilityProfile.ProcessDurable);
        await SafeProjectorOperationSeed.EqualAsync(actual, expected);
        await TokenAsync(partition, actual.Token);
    }

    internal static async Task ReplayAsync(CommitReceipt expected, CommitReceipt actual)
        => await SafeProjectorOperationSeed.EqualAsync(actual, expected);

    internal static async Task CutsAsync(TestDatabase fixture, StoreIdentity originalIdentity, long originalCut)
    {
        var identity = fixture.Store.Identity;
        await Assert.That(fixture.Store.Position).IsGreaterThan(originalCut);
        await Assert.That(identity.NodeId).IsEqualTo(originalIdentity.NodeId);
        await Assert.That(identity.Incarnation).IsEqualTo(originalIdentity.Incarnation);
        await Assert.That(identity.FormatVersion).IsEqualTo(originalIdentity.FormatVersion);
        await Assert.That(identity.KeyCodecVersion).IsEqualTo(originalIdentity.KeyCodecVersion);
        await Assert.That(identity.ReadGeneration).IsEqualTo(originalIdentity.ReadGeneration);
        await Assert.That(identity.MinimumReaderContract).IsEqualTo(originalIdentity.MinimumReaderContract);
        await Assert.That(identity.Durability).IsEqualTo(originalIdentity.Durability);
    }

    internal static async Task JournalAsync(TestDatabase fixture, CommandRequest command, CommitReceipt receipt)
    {
        var entry = fixture.ReadRetainedEntry(command.CommandId);
        await Assert.That(entry.Index).IsEqualTo(receipt.Token.Position);
        await Assert.That(entry.Operation is not null).IsTrue();
        await Assert.That(entry.Operation!.Id).IsEqualTo(command.CommandId);
    }

    private static async Task TokenAsync(PartitionRef partition, CommitToken token)
    {
        await Assert.That(token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(token.Incarnation != Guid.Empty).IsTrue();
        await Assert.That(token.Position).IsGreaterThan(0L);
        await Assert.That(token.OwnershipEpoch).IsGreaterThan(0L);
    }
}
