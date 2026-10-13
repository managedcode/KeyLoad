using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3BackpressureAssertions
{
    internal static async Task PageAsync(PostgresRf3BackpressureState state, QueryPage actual)
    {
        var rows = Enumerable.Range(PostgresRf3Protocol.OneColumn, PostgresRf3BackpressureProtocol.Rows)
            .Select(index => new QueryRow(PostgresRf3BackpressureSeed.Id(index), PostgresRf3Protocol.SeedRevision,
                PostgresRf3BackpressureSeed.Json(index), RedactedFields: [])).ToImmutableArray();
        await SqlRf3Protocol.EqualAsync(new QueryPage(rows, null, actual.CutPosition, AccessPath), actual);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
    }

    internal static async Task<string> ImageAsync(KeyLoadClient root, CancellationToken token)
    {
        var originals = await PostgresRf3Assertions.ImageAsync(root, token);
        var rows = new List<DocumentResult?>();
        foreach (var index in Enumerable.Range(PostgresRf3Protocol.OneColumn, PostgresRf3BackpressureProtocol.Rows))
        {
            var reference = new EntityRef(PostgresRf3State.Partition, PostgresRf3BackpressureProtocol.Collection,
                PostgresRf3BackpressureSeed.Id(index));
            var actual = await PublicReadAssertions.DocumentAsync(await root.GetAsync(reference, token));
            await SqlRf3Protocol.EqualAsync<DocumentResult?>(new(reference, PostgresRf3Protocol.SeedRevision,
                PostgresRf3BackpressureSeed.Json(index), false, []), actual);
            rows.Add(actual);
        }
        return JsonSerializer.Serialize(new { Original = originals, Rows = rows }, JsonDefaults.Options);
    }

    internal static async Task ReceiptAsync(PostgresRf3BackpressureState state, CommitReceipt actual)
    {
        var effects = Enumerable.Range(PostgresRf3Protocol.OneColumn, PostgresRf3BackpressureProtocol.Rows)
            .Select(index => new MutationReceipt(PutKind, PostgresRf3BackpressureProtocol.Collection,
                PostgresRf3BackpressureSeed.Id(index), PostgresRf3Protocol.SeedRevision)).ToImmutableArray();
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(state.Command.CommandId, state.Receipt.Token,
            effects, DurabilityProfile.QuorumProcessDurable), actual);
        await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(PostgresRf3State.Partition.AtomicPartitionId);
        await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(PostgresRf3Protocol.FirstColumn);
    }

    private const string AccessPath = "bounded-full-scan";
    private const string PutKind = "putDocument";
}
