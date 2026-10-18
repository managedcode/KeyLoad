using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlFetchCountClusterAssertions
{
    private const string PutKind = "putDocument";
    private const string AccessPath = "bounded-full-scan";
    internal static async Task PageAsync(QueryPage actual, int count)
    {
        string[] ids = ["a", "b", "c"];
        if (count < SqlFetchCountClusterProtocol.RowCount)
        {
            await global::TUnit.Assertions.Assert.That(actual.Cursor).IsNotNull();
        }
        else
        {
            await global::TUnit.Assertions.Assert.That(actual.Cursor).IsNull();
        }
        var expected = new QueryPage([.. ids.Take(count).Select(id => new QueryRow(id,
            SqlFetchCountClusterProtocol.SeedRevision, "{\"label\":\"" + id + "\"}", RedactedFields: []))],
            actual.Cursor, actual.CutPosition, AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
    internal static async Task ReceiptAsync(CommandRequest original, CommitReceipt actual)
    {
        var expected = new CommitReceipt(original.CommandId, actual.Token,
            [.. SqlFetchCountClusterState.Rows.Select(row => new MutationReceipt(PutKind,
                SqlFetchCountClusterProtocol.Collection, row.Id, SqlFetchCountClusterProtocol.SeedRevision))],
            DurabilityProfile.QuorumProcessDurable);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
    }
    internal static async Task<string> ImageAsync(KeyLoadClient root, SqlFetchCountClusterState state, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        foreach (var row in SqlFetchCountClusterState.Rows)
        {
            var reference = new EntityRef(PostgresClusterState.Partition, SqlFetchCountClusterProtocol.Collection, row.Id);
            var expected = new DocumentReadResult(new(reference, SqlFetchCountClusterProtocol.SeedRevision, row.Json, false, []));
            var actual = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(reference, state.Receipt.Token, token));
            await SqlClusterProtocol.EqualAsync(expected, actual);
            documents.Add(actual);
        }
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(PostgresClusterState.Partition, token));
        return global::System.Text.Json.JsonSerializer.Serialize(new
        {
            Documents = documents,
            Outbox = outbox
        }, JsonDefaults.Options);
    }
}
