using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNullOrderingClusterAssertions
{
    private const string PutKind = "putDocument";
    private const string AccessPath = "bounded-full-scan";
    internal static async Task PageAsync(QueryPage actual, bool descending, bool first)
    {
        string[] nonnull = descending ? ["high", "low", "missing"] : ["missing", "low", "high"];
        string[] ids = first ? ["null_a", "null_b", .. nonnull] : [.. nonnull, "null_a", "null_b"];
        var expected = new QueryPage([.. ids.Select(id => new QueryRow(id, SqlNullOrderingClusterProtocol.SeedRevision,
            "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, actual.CutPosition, AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
    internal static async Task ReceiptAsync(CommandRequest original, CommitReceipt actual)
    {
        var expected = new CommitReceipt(original.CommandId, actual.Token,
            [.. SqlNullOrderingClusterState.Rows.Select(row => new MutationReceipt(PutKind,
                SqlNullOrderingClusterProtocol.Collection, row.Id, SqlNullOrderingClusterProtocol.SeedRevision))],
            DurabilityProfile.ProcessDurable, 3);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
    }
    internal static async Task<string> ImageAsync(KeyLoadClient root, SqlNullOrderingClusterState state, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        foreach (var row in SqlNullOrderingClusterState.Rows)
        {
            var reference = new EntityRef(PostgresClusterState.Partition, SqlNullOrderingClusterProtocol.Collection, row.Id);
            var expected = new DocumentReadResult(new(state.Stored(row.Id), SqlNullOrderingClusterProtocol.SeedRevision, row.Json, false, []));
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
