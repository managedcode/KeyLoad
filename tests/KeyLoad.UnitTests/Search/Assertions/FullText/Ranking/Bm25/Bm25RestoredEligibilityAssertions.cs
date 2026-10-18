using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.BackupRestore;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25RestoredEligibilityAssertions
{
    internal static (byte[] Identity, long Position, long Applied,
        (long AppliedPosition, long StorePosition) CanonicalCut, string[] Rows) Cut(
        IAtomicStore store, DatabaseEngine database,
        (long AppliedPosition, long StorePosition) canonicalCut, CancellationToken token)
        => (NativeSerialization.Serialize(store.Identity), store.Position, database.LastApplied, canonicalCut,
            Bm25PortableFamilyBytes.Complete(store, database.EvaluationClock, token));

    internal static async Task SameAsync(
        (byte[] Identity, long Position, long Applied, (long, long) CanonicalCut, string[] Rows) expected,
        (byte[] Identity, long Position, long Applied, (long, long) CanonicalCut, string[] Rows) actual)
    {
        await Assert.That(actual.Identity.AsSpan().SequenceEqual(expected.Identity)).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Applied).IsEqualTo(expected.Applied);
        await Assert.That(actual.CanonicalCut).IsEqualTo(expected.CanonicalCut);
        await Bm25PortableFamilyBytes.SameAsync(expected.Rows, actual.Rows);
    }

    internal static DistributedTextStatisticsV1? Current(EventingArtifactFixture fixture, string subject)
        => fixture.Database.WithQueryView(subject, fixture.Source.Partition, Bm25PortableFamilyBackupSeed.Collection,
            (view, principal, resource) => fixture.Database.ReadBm25CorpusCurrent(view, principal, resource,
                fixture.Source.Partition, Bm25PortableFamilyBackupSeed.Collection, Bm25PortableFamilyBackupSeed.Field,
                TextProjectionProtocol.TokenizerVersion, ["needle"], fixture.Source.CreateReadWork(Token)));

    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    internal static async Task<RankedDocument[]> SearchAsync(DatabaseEngine database, PartitionRef partition,
        string subject, CancellationToken token)
    {
        var rows = await new SearchEngine(database, UnitExecutionOptions.QueryExecution()).SearchAsync(subject,
            new(partition, Bm25PortableFamilyBackupSeed.Collection, Bm25PortableFamilyBackupSeed.Field, "needle"), token);
        await Assert.That(rows.Length).IsEqualTo(1);
        var first = subject == Bm25PortableFamilyBackupSeed.FirstSubject;
        await Assert.That(rows[0].Document.Reference.Id).IsEqualTo(first ? "first" : "second");
        await Assert.That(rows[0].Document.Json).IsEqualTo(first
            ? """{"text":"needle alpha"}""" : """{"text":"needle beta gamma"}""");
        await Assert.That(rows[0].Document.Redacted).IsFalse();
        return rows;
    }

    internal static async Task ReplayAsync(EventingArtifactFixture fixture, bool source,
        Command<DatabaseCommand> operation, CommitReceipt receipt, CancellationToken token)
    {
        var store = source ? fixture.Source.Store : fixture.Target!;
        var database = source ? fixture.Source.Database : fixture.Database;
        var before = Cut(store, database, source ? fixture.Source.ReadCanonicalCut() : fixture.ReadTargetCut(), token);
        var retained = source ? fixture.Source.ReadStoredOutcome(operation.CommandId).PreparedCommand!
            : fixture.ReadTargetIssuedOperation(operation.CommandId);
        await Assert.That(retained.Value!.Input.Span.SequenceEqual(operation.Value!.Input.Span)).IsTrue();
        await Assert.That(operation.Value!.Input.IsEmpty).IsFalse();
        await Assert.That(NativeSerialization.Serialize(retained).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(operation))).IsTrue();
        var actual = fixture.SubmitIssued(database, operation, cancellationToken: token).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        var stored = OutcomeStoreOracle.ReadPartition(store, fixture.Source.Partition,
            Bm25PortableFamilyBackupSeed.Root, operation.CommandId)!.Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(stored).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await SameAsync(before, Cut(store, database,
            source ? fixture.Source.ReadCanonicalCut() : fixture.ReadTargetCut(), token));
    }
}
