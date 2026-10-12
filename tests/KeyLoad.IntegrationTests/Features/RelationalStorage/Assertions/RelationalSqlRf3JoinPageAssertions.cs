using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

using Q2AdmissionRouting = global::KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Checks exact two-source metadata on every page returned by an RF3 SQL join caller.</summary>
internal static class RelationalSqlRf3JoinPageAssertions
{
    private const int SourceCount = 2;
    private const int LeftSourceIndex = 0;
    private const int RightSourceIndex = 1;
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string CustomerId = "customer_id";

    internal static async Task EquivalentAsync(QueryPage expected, QueryPage actual)
    {
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await VerifySourcesAsync(actual);
    }

    internal static async Task SameRowsAsync(QueryPage expected, QueryPage actual)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(expected.CutPosition);
        await SqlRf3Protocol.EqualAsync(expected with { CutPosition = actual.CutPosition }, actual);
        await VerifySourcesAsync(actual);
    }

    internal static async Task RevisionsAsync(QueryPage page, long expectedLeftRevision, long expectedRightRevision)
    {
        await VerifySourcesAsync(page);
        foreach (var row in page.Rows)
        {
            var sources = row.Sources!.Value;
            await Assert.That(sources[LeftSourceIndex].Revision).IsEqualTo(expectedLeftRevision);
            await Assert.That(sources[RightSourceIndex].Revision).IsEqualTo(expectedRightRevision);
        }
    }

    internal static async Task VerifySourcesAsync(QueryPage page)
    {
        foreach (var row in page.Rows)
        {
            await Assert.That(row.Sources.HasValue).IsTrue();
            var sources = row.Sources!.Value;
            await Assert.That(sources.Length).IsEqualTo(SourceCount);
            await Assert.That(sources[LeftSourceIndex].Alias).IsEqualTo(LeftAlias);
            await Assert.That(sources[LeftSourceIndex].EntityId).IsEqualTo(row.EntityId);
            await Assert.That(sources[LeftSourceIndex].Revision).IsEqualTo(row.Revision);
            await Assert.That(sources[RightSourceIndex].Alias).IsEqualTo(RightAlias);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(sources[RightSourceIndex].EntityId)
                .IsEqualTo(json.RootElement.GetProperty(CustomerId).GetString());
            await Assert.That(sources[RightSourceIndex].Revision).IsGreaterThan(0);
        }
    }
}

internal static class RelationalSqlRf3JoinAdmissionAssertions
{
    internal static async Task<string[]> CaptureAsync(KeyLoadClient admin,
        RelationalQ2AdmissionSeed seed, CancellationToken token)
    {
        var partition = seed.Scenario.Partition;
        var left = await McpCallerAssertions.SdkSuccessAsync(await admin.GetAsync(
            new(partition, RelationalSqlRf3Tokens.Table, RelationalSqlRf3JoinAdmissionSeed.Order), token));
        var right = await McpCallerAssertions.SdkSuccessAsync(await admin.GetAsync(
            new(partition, RelationalSqlRf3JoinAdmissionSeed.Right, RelationalSqlRf3JoinAdmissionSeed.Customer), token));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.OutboxStatusAsync(partition, token));
        var page = await McpCallerAssertions.SdkSuccessAsync(await admin.QueryAstAsync(seed.Ast, token));
        await LiteralAsync(page, seed.Receipt.Token.Position);
        return [Convert.ToHexString(JsonDefaults.Serialize(left)), Convert.ToHexString(JsonDefaults.Serialize(right)),
            Convert.ToHexString(JsonDefaults.Serialize(outbox)), Convert.ToHexString(JsonDefaults.Serialize(page))];
    }

    internal static async Task UnchangedAsync(KeyLoadClient admin, RelationalQ2AdmissionSeed seed,
        string[] original, CancellationToken token)
    {
        var actual = await CaptureAsync(admin, seed, token);
        await Assert.That(actual.SequenceEqual(original, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task HealthyAsync(Q2AdmissionRouting.RequestCqrsRf3Callers caller,
        RelationalQ2AdmissionSeed seed, CancellationToken token)
    {
        var cut = seed.Receipt.Token.Position;
        await LiteralAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, seed.Sql, token), cut);
        await LiteralAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(seed.Ast, token)), cut);
        await LiteralAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAsync(seed.Query, token)), cut);
        await LiteralAsync(await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, seed.Sql, token), cut);
        await LiteralAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryAst, seed.Ast, token))).Value, cut);
        await LiteralAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryExecute, seed.Query, token))).Value, cut);
    }

    private static async Task LiteralAsync(QueryPage actual, long cut)
    {
        var expected = new QueryPage([new(RelationalSqlRf3JoinAdmissionSeed.Order,
            RelationalSqlRf3JoinAdmissionSeed.Revision,
            "{\"order_key\":\"cancelled-join-order\",\"customer_name\":\"Ada\",\"customer_id\":\"cancelled-join-customer\"}",
            Redacted: false, RedactedFields: [],
            Sources: [new("l", RelationalSqlRf3JoinAdmissionSeed.Order, RelationalSqlRf3JoinAdmissionSeed.Revision),
                new("r", RelationalSqlRf3JoinAdmissionSeed.Customer, RelationalSqlRf3JoinAdmissionSeed.Revision)])],
            null, cut, "bounded-primary-key-inner-join");
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }

    internal static async Task CancelledAsync(RelationalQ2CancellationOutcome actual, bool officialMcp)
    {
        await Assert.That(actual.ReturnedSuccess).IsFalse();
        if (officialMcp)
        {
            await Assert.That(actual.Cancellation).IsNotNull();
            await Assert.That(actual.ErrorCode).IsNull();
        }
        else
        {
            await Assert.That(actual.Cancellation).IsNull();
            await Assert.That(actual.ErrorCode).IsEqualTo(nameof(ErrorCode.Cancelled));
        }
    }
}
