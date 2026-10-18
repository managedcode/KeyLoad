using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Search;

internal sealed record Bm25ConfiguredQuotaRetainedEntries(byte[][] Entries)
{
    internal static Bm25ConfiguredQuotaRetainedEntries Read(TestDatabase database, Command<DatabaseCommand>[] publications)
    {
        var cut = database.ReadCanonicalCut();
        if (publications.Length == 0 || publications.Length > Bm25ConfiguredQuotaFlow.HeadCount
            || publications.Select(operation => operation.Id).Distinct().Count() != publications.Length
            || cut.LastIndex > database.Database.Limits.MaxScanRecords)
        {
            throw new InvalidOperationException("The original admitted publication observation is not bounded.");
        }
        var work = database.CreateReadWork(Bm25ConfiguredQuotaFlow.Token);
        var entries = new byte[publications.Length][];
        for (var index = 0; index < publications.Length; index++)
        {
            var original = publications[index];
            var entry = database.ReadStoredOutcome(original.Id);
            if (original.Kind != OperationKind.Bm25CorpusPublicationPhase
                || entry.PreparedCommand is not { } operation || operation.Id != original.Id
                || operation.Kind != original.Kind
                || !operation.NativePayload.Span.SequenceEqual(original.NativePayload.Span))
            {
                throw new InvalidOperationException("The admitted native entry is not its actual original publication.");
            }
            work.ChargeBytes(NativeSerialization.Measure(entry));
            entries[index] = NativeSerialization.Serialize(entry);
        }
        return new(entries);
    }

    internal async Task RequireAsync(TestDatabase database, Command<DatabaseCommand>[] publications)
    {
        var current = Read(database, publications);
        await Assert.That(current.Entries.Length).IsEqualTo(Entries.Length);
        for (var index = 0; index < Entries.Length; index++)
        {
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(Entries[index], current.Entries[index]);
        }
    }
}
