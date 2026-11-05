using System.Globalization;
using KeyLoad.Client;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Query;

namespace KeyLoad.ClusterTests.DocumentStorage;

internal static class DocumentCommitObservation
{
    private const string IdPath = "/@id";

    internal static async Task<(long Commands, long Commits)> ReadAsync(KeyLoadClient client,
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
        (long Commands, long Commits) before, int records, int batchRecords, string output, CancellationToken token)
    {
        var after = await ReadAsync(client, partition, token);
        var commands = after.Commands - before.Commands;
        var commits = after.Commits - before.Commits;
        var expected = (records + batchRecords - 1) / batchRecords;
        var quality = commands == expected && commits > 0 && commits <= commands ? "matched" : "unavailable";
        var evidence = string.Create(CultureInfo.InvariantCulture,
            $"quality={quality} documents={records} commandBatches={commands} physicalCommits={commits} batchRecords={batchRecords}\n"
            + $"beforeCommands={before.Commands} beforeCommits={before.Commits} afterCommands={after.Commands} afterCommits={after.Commits}\n");
        await File.WriteAllTextAsync(Path.Combine(output, "bulk-commit-counts.txt"), evidence, token);
        await Console.Out.WriteAsync("Local SDK commit observation: " + evidence);
    }
}
