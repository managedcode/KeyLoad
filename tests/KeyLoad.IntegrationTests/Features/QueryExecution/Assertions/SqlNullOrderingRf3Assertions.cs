using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullOrderingRf3Assertions
{
    private const string PutKind = "putDocument";
    private const string AccessPath = "bounded-full-scan";
    internal static async Task PageAsync(QueryPage actual, bool descending, bool first)
    {
        string[] nonnull = descending ? ["high", "low", "missing"] : ["missing", "low", "high"];
        string[] ids = first ? ["null_a", "null_b", .. nonnull] : [.. nonnull, "null_a", "null_b"];
        var expected = new QueryPage([.. ids.Select(id => new QueryRow(id, SqlNullOrderingRf3Protocol.SeedRevision,
            "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, actual.CutPosition, AccessPath);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
    internal static async Task ReceiptAsync(CommandRequest original, CommitReceipt actual)
    {
        var expected = new CommitReceipt(original.CommandId, actual.Token,
            [.. SqlNullOrderingRf3State.Rows.Select(row => new MutationReceipt(PutKind,
                SqlNullOrderingRf3Protocol.Collection, row.Id, SqlNullOrderingRf3Protocol.SeedRevision))],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
    }
    internal static async Task<string> ImageAsync(KeyLoadClient root, SqlNullOrderingRf3State state, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        foreach (var row in SqlNullOrderingRf3State.Rows)
        {
            var reference = new EntityRef(PostgresRf3State.Partition, SqlNullOrderingRf3Protocol.Collection, row.Id);
            var expected = new DocumentReadResult(new(reference, SqlNullOrderingRf3Protocol.SeedRevision, row.Json, false, []));
            var actual = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(reference, state.Receipt.Token, token));
            await SqlRf3Protocol.EqualAsync(expected, actual);
            documents.Add(actual);
        }
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(PostgresRf3State.Partition, token));
        return global::System.Text.Json.JsonSerializer.Serialize(new { Documents = documents, Outbox = outbox }, JsonDefaults.Options);
    }
}
