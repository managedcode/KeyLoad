using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Assertions
{
    private const string PutKind = "putDocument";
    private const string ScalarAccess = "bounded-full-scan";
    private const string JoinAccess = "bounded-primary-key-inner-join";

    internal static async Task ReceiptAsync(CommandRequest original, CommitReceipt actual)
    {
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(original.CommandId, actual.Token,
            [new(PutKind, PostgresRf3State.Left.Collection, PostgresRf3State.Left.Id, PostgresRf3Protocol.SeedRevision),
             new(PutKind, PostgresRf3State.Right.Collection, PostgresRf3State.Right.Id, PostgresRf3Protocol.SeedRevision)],
            DurabilityProfile.QuorumProcessDurable), actual);
        await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(PostgresRf3Protocol.FirstColumn);
    }

    internal static async Task PageAsync(PostgresRf3State state, QueryPage actual)
    {
        QueryRow row = state.Dialect == PostgresRf3Protocol.JoinDialect
            ? new(PostgresRf3State.OrderId, PostgresRf3Protocol.SeedRevision, PostgresRf3State.JoinedJson,
                RedactedFields: [], Sources: [new("l", PostgresRf3State.OrderId, PostgresRf3Protocol.SeedRevision),
                    new("r", PostgresRf3State.CustomerId, PostgresRf3Protocol.SeedRevision)])
            : new(PostgresRf3State.OrderId, PostgresRf3Protocol.SeedRevision, PostgresRf3State.LeftJson, RedactedFields: []);
        var expected = new QueryPage([row], null, actual.CutPosition,
            state.Dialect == PostgresRf3Protocol.JoinDialect ? JoinAccess : ScalarAccess);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }

    internal static async Task<string> ImageAsync(KeyLoadClient root, CancellationToken token)
    {
        var left = await PublicReadAssertions.DocumentAsync(await root.GetAsync(PostgresRf3State.Left, token));
        var right = await PublicReadAssertions.DocumentAsync(await root.GetAsync(PostgresRf3State.Right, token));
        await SqlRf3Protocol.EqualAsync<DocumentResult?>(new(PostgresRf3State.Left,
            PostgresRf3Protocol.SeedRevision, PostgresRf3State.LeftJson, false, []), left);
        await SqlRf3Protocol.EqualAsync<DocumentResult?>(new(PostgresRf3State.Right,
            PostgresRf3Protocol.SeedRevision, PostgresRf3State.RightJson, false, []), right);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await root.OutboxStatusAsync(PostgresRf3State.Partition, token));
        return System.Text.Json.JsonSerializer.Serialize(new { Left = left, Right = right, Outbox = outbox },
            JsonDefaults.Options);
    }

    internal static async Task MinimumTokenAsync(ClusterFixture fixture, PostgresRf3State state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            foreach (var reference in new[] { PostgresRf3State.Left, PostgresRf3State.Right })
            {
                var json = reference == PostgresRf3State.Left ? PostgresRf3State.LeftJson : PostgresRf3State.RightJson;
                var expected = new DocumentReadResult(new DocumentResult(reference,
                    PostgresRf3Protocol.SeedRevision, json, false, []));
                await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                    await root.Sdk.GetAsync(reference, state.Receipt.Token, token)));
                await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                    await root.Mcp.CallAsync(McpCallerTools.DocumentsGet,
                        new GetDocumentRequest(reference, state.Receipt.Token), token))).Value);
            }
            return true;
        }, token);
    }

    internal static async Task ErrorAsync(Task operation, string sqlState, ErrorCode code)
    {
        var failure = await global::TUnit.Assertions.Assert.ThrowsAsync<global::Npgsql.PostgresException>(() => operation)
            ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        await global::TUnit.Assertions.Assert.That(failure.SqlState).IsEqualTo(sqlState);
        await global::TUnit.Assertions.Assert.That(failure.MessageText).IsEqualTo(code.ToString());
    }
}
