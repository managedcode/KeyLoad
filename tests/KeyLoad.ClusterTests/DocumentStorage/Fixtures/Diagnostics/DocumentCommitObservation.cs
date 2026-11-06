using System.Globalization;
using KeyLoad.Client;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Query;

namespace KeyLoad.ClusterTests.DocumentStorage;

internal static class DocumentCommitObservation
{
    private const string IdPath = "/@id";

    internal static async Task<(long Commands, long SnapshotTimestamp)> ReadAsync(KeyLoadClient client,
        PartitionRef partition, CancellationToken token)
    {
        var status = ClusterReplicationTestSupport.Success(await client.StatusAsync(token));
        var filter = new Comparison(new FieldOperand(IdPath), "=",
            ValueOperand.Create(DocumentPathDiagnosticCorpus.Id(0)));
        var query = new SelectQuery(DocumentPathDiagnosticCorpus.Collection, null, [new(IdPath, "id")], filter, [], 1);
        var page = ClusterReplicationTestSupport.Success(await client.QueryAstAsync(new(partition, query), token));
        return (status.Position, page.CutPosition);
    }

    internal static async Task SaveAsync(KeyLoadClient client, PartitionRef partition,
        (long Commands, long SnapshotTimestamp) before, int records, int batchRecords, string output, CancellationToken token)
    {
        var after = await ReadAsync(client, partition, token);
        var commands = after.Commands - before.Commands;
        var timestampAdvance = after.SnapshotTimestamp - before.SnapshotTimestamp;
        var expected = (records + batchRecords - 1) / batchRecords;
        var quality = commands == expected && timestampAdvance > 0 ? "matched" : "unavailable";
        var evidence = string.Create(CultureInfo.InvariantCulture,
            $"quality={quality} documents={records} commandBatches={commands} nativeSnapshotTimestampAdvance={timestampAdvance} batchRecords={batchRecords}\n"
            + $"beforeCommands={before.Commands} beforeSnapshotTimestamp={before.SnapshotTimestamp} afterCommands={after.Commands} afterSnapshotTimestamp={after.SnapshotTimestamp}\n");
        await File.WriteAllTextAsync(Path.Combine(output, "bulk-commit-counts.txt"), evidence, token);
        await Console.Out.WriteAsync("Local SDK commit observation: " + evidence);
    }
}
