using KeyLoad.Core.Features.Search;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25ConfiguredQuotaBytes
{
    internal static long Retained(TestDatabase database)
        => database.Store.Read(view =>
        {
            var page = view.Scan(Bm25CorpusKeys.Prefix(database.Partition), database.Database.Limits.MaxScanRecords, null);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The complete actual corpus family exceeded its original bound.");
            }
            return page.Records.Sum(row => checked((long)row.Key.Length + row.Value.Length));
        });

    internal static long Candidate(Command<DatabaseCommand> operation)
    {
        var current = Bm25ConfiguredQuotaFlow.Phase(operation).Current;
        return checked((long)Bm25CorpusKeys.Current(current).Length + OrleansSerialization.Serialize(current).Length);
    }

    internal static async Task RequireStoredAsync(TestDatabase database, Command<DatabaseCommand> operation, Result<object> result,
        (CommitToken Token, long Applied) expected)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var phase = Bm25ConfiguredQuotaFlow.Phase(operation);
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(receipt.Token).IsEqualTo(expected.Token);
        await Assert.That(database.ReadStoredOutcome(operation.CommandId).CanonicalSequence).IsEqualTo(0L);
        await Assert.That(database.Database.LastApplied).IsEqualTo(expected.Applied);
        await Assert.That(database.Store.Position).IsEqualTo(expected.Token.Position);
        var expectedReceipt = new CommitReceipt(operation.CommandId, expected.Token, [],
            database.Database.Durability, database.Database.AcknowledgedPersistedCopies);
        await ReplayResultAssertions.BytesAsync(OrleansSerialization.Serialize(expectedReceipt), OrleansSerialization.Serialize(receipt));
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(database.Database.Durability);
        var actual = database.Store.Read(view => view.ReadOwnedValue(Bm25CorpusKeys.Current(phase.Current)))
            ?? throw new InvalidOperationException("The genuinely published corpus head is missing.");
        await ReplayResultAssertions.BytesAsync(OrleansSerialization.Serialize(phase.Current), actual);
        await ReplayResultAssertions.SameAsync(result, database.Database.ResolveSubmissionOutcome(
            database.ReadStoredOutcome(operation.CommandId).PreparedCommand!));
    }
}
