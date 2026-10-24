using KeyLoad.UnitTests.ResourceExecution;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Search;

internal sealed record Bm25ConfiguredQuotaRetainedEntries(byte[][] Entries)
{
    internal static Bm25ConfiguredQuotaRetainedEntries Read(TestDatabase database, Command<DatabaseCommand>[] publications)
    {
        var cut = database.ReadCanonicalCut();
        if (publications.Length == 0 || publications.Length > Bm25ConfiguredQuotaFlow.HeadCount
            || publications.Select(operation => operation.CommandId).Distinct().Count() != publications.Length
            || cut.AppliedPosition > database.Database.Limits.MaxScanRecords)
        {
            throw new InvalidOperationException("The original admitted publication observation is not bounded.");
        }
        var work = database.CreateReadWork(Bm25ConfiguredQuotaFlow.Token);
        var entries = new byte[publications.Length][];
        for (var index = 0; index < publications.Length; index++)
        {
            var original = publications[index];
            var entry = database.ReadStoredOutcome(original.CommandId);
            if (original.Value!.Kind != OperationKind.Bm25CorpusPublicationPhase
                || entry.PreparedCommand is not { } operation || operation.CommandId != original.CommandId
                || operation.Value!.Kind != original.Value!.Kind
                || !operation.Value!.Input.Span.SequenceEqual(original.Value!.Input.Span))
            {
                throw new InvalidOperationException("The admitted execution entry is not its actual original publication.");
            }
            work.ChargeBytes(OrleansSerialization.Measure(entry));
            entries[index] = OrleansSerialization.Serialize(entry);
        }
        return new(entries);
    }

    internal async Task RequireAsync(TestDatabase database, Command<DatabaseCommand>[] publications)
    {
        var current = Read(database, publications);
        await Assert.That(current.Entries.Length).IsEqualTo(Entries.Length);
        for (var index = 0; index < Entries.Length; index++)
        {
            await ReplayResultAssertions.BytesAsync(Entries[index], current.Entries[index]);
        }
    }
}
