using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
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
        return checked((long)Bm25CorpusKeys.Current(current).Length + NativeSerialization.Serialize(current).Length);
    }

    internal static async Task RequireStoredAsync(TestDatabase database, Command<DatabaseCommand> operation, Result<object> result)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var phase = Bm25ConfiguredQuotaFlow.Phase(operation);
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(database.ReadStoredOutcome(operation.CommandId).CanonicalSequence);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(database.Database.Durability);
        var actual = database.Store.Read(view => view.ReadOwnedValue(Bm25CorpusKeys.Current(phase.Current)))
            ?? throw new InvalidOperationException("The genuinely published corpus head is missing.");
        await NativeReplayResultAssertions.BytesAsync(NativeSerialization.Serialize(phase.Current), actual);
        await NativeReplayResultAssertions.SameAsync(result, database.Database.ResolveSubmissionOutcome(
            database.ReadStoredOutcome(operation.CommandId).PreparedCommand!));
    }
}
