using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClusterAssertions
{
    private const string PutKind = "putDocument";

    internal static async Task ReceiptAsync(CommandRequest original, CommitReceipt actual)
    {
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(original.CommandId, actual.Token,
            [new(PutKind, PostgresClusterState.Left.Collection, PostgresClusterState.Left.Id, PostgresClusterProtocol.SeedRevision),
             new(PutKind, PostgresClusterState.Right.Collection, PostgresClusterState.Right.Id, PostgresClusterProtocol.SeedRevision)],
            DurabilityProfile.ProcessDurable, 3), actual);
        await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(PostgresClusterProtocol.FirstColumn);
    }

    internal static async Task<string> ImageAsync(KeyLoadClient root, CancellationToken token)
    {
        var left = await PublicReadAssertions.DocumentAsync(await root.GetAsync(PostgresClusterState.Left, token));
        var right = await PublicReadAssertions.DocumentAsync(await root.GetAsync(PostgresClusterState.Right, token));
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(new(PostgresClusterState.Left,
            PostgresClusterProtocol.SeedRevision, PostgresClusterState.LeftJson, false, []), left);
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(new(PostgresClusterState.Right,
            PostgresClusterProtocol.SeedRevision, PostgresClusterState.RightJson, false, []), right);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await root.OutboxStatusAsync(PostgresClusterState.Partition, token));
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Left = left,
            Right = right,
            Outbox = outbox
        },
            JsonDefaults.Options);
    }

    internal static async Task ErrorAsync(Task operation, string sqlState, ErrorCode code)
    {
        var failure = await global::TUnit.Assertions.Assert.ThrowsAsync<global::Npgsql.PostgresException>(() => operation)
            ?? throw new InvalidOperationException(PostgresClusterProtocol.InvalidResult);
        await global::TUnit.Assertions.Assert.That(failure.SqlState).IsEqualTo(sqlState);
        await global::TUnit.Assertions.Assert.That(failure.MessageText).IsEqualTo(code.ToString());
    }
}
