using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipAssertions
{
    internal static async Task PageAsync(PostgresRf3State state, QueryPage actual)
    {
        QueryRow row = state.Dialect == PostgresRf3Protocol.JoinDialect
            ? new(PostgresRf3State.OrderId, PostgresRf3Protocol.SeedRevision, PostgresRf3State.JoinedJson,
                RedactedFields: [], Sources: [new(PostgresPortalOwnershipProtocol.LeftAlias, PostgresRf3State.OrderId, PostgresRf3Protocol.SeedRevision),
                    new(PostgresPortalOwnershipProtocol.RightAlias, PostgresRf3State.CustomerId, PostgresRf3Protocol.SeedRevision)])
            : new(PostgresRf3State.OrderId, PostgresRf3Protocol.SeedRevision, PostgresRf3State.LeftJson, RedactedFields: []);
        await global::TUnit.Assertions.Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(default(long));
        await SqlRf3Protocol.EqualAsync(new QueryPage([row], null, actual.CutPosition,
            state.Dialect == PostgresRf3Protocol.JoinDialect ? JoinAccess : ScalarAccess), actual);
    }

    internal static async Task<string> ImageAsync(KeyLoadClient root, PostgresRf3State state, CancellationToken token)
    {
        var left = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(PostgresRf3State.Left, state.Receipt.Token, token));
        var right = await McpCallerAssertions.SdkSuccessAsync(await root.GetAsync(PostgresRf3State.Right, state.Receipt.Token, token));
        await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new(PostgresRf3State.Left,
            PostgresRf3Protocol.SeedRevision, PostgresRf3State.LeftJson, false, [])), left);
        await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new(PostgresRf3State.Right,
            PostgresRf3Protocol.SeedRevision, PostgresRf3State.RightJson, false, [])), right);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(PostgresRf3State.Partition, token));
        return global::System.Text.Json.JsonSerializer.Serialize(new { Left = left, Right = right, Outbox = outbox }, JsonDefaults.Options);
    }

    private const string JoinAccess = "bounded-primary-key-inner-join";
    private const string ScalarAccess = "bounded-full-scan";
}
