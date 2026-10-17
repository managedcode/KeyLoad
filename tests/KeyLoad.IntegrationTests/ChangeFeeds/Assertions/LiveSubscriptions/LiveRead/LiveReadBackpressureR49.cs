using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveReadBackpressureR49
{
    private const int OneByte = 1;
    private const string FirstChangeDetail = "The first change exceeds the page byte budget.";
    private const string SafeBudgetDetail = "The operation exceeds the available resource capacity.";
    private const string DetailKey = "detail";
    private const long PositiveCutBoundary = 0;

    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadLiveQueryRequest original,
        CommitReceipt deletion, CancellationToken token)
    {
        var refused = original with
        {
            MaxBytes = OneByte
        };
        await CurrentAsync(administrator, scenario, async () =>
        {
            var result = await reader.Sdk.ReadLiveQueryAsync(refused, token);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.BudgetExceeded, FirstChangeDetail), result.Problem);
        }, token);
        await HealthyAsync(administrator, reader, scenario, original, deletion, false, token);
        await CurrentAsync(administrator, scenario, async () =>
        {
            var reply = await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, refused, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.BudgetExceeded, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(DetailKey).GetString()).IsEqualTo(SafeBudgetDetail);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
        }, token);
        await HealthyAsync(administrator, reader, scenario, original, deletion, true, token);
    }

    private static async Task HealthyAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadLiveQueryRequest original,
        CommitReceipt deletion, bool useMcp, CancellationToken token)
    {
        LiveQueryPage? delivered = null;
        await CurrentAsync(administrator, scenario, async () =>
        {
            delivered = await ReadAsync(reader, scenario, original, useMcp, token);
            await PageAsync(delivered, [new(FeedLiveRf3Protocol.DeletedSequence, deletion.Token,
                LiveQueryChangeKind.Remove, scenario.Reference, FeedLiveRf3Protocol.DeletedRevision, null)]);
        }, token);
        var consumed = original with
        {
            Cursor = delivered!.Cursor
        };
        await CurrentAsync(administrator, scenario, async () =>
            await PageAsync(await ReadAsync(reader, scenario, consumed, useMcp, token), []), token);
    }

    private static async Task<LiveQueryPage> ReadAsync(RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, ReadLiveQueryRequest request, bool useMcp, CancellationToken token)
    {
        if (!useMcp)
        {
            return await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token));
        }
        var reply = await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, request, token);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, scenario.Identity.Secret, FeedLiveRf3Protocol.Secret);
        return (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(reply)).Value;
    }

    private static async Task PageAsync(LiveQueryPage page, LiveQueryChange[] expected)
    {
        await SqlRf3Protocol.EqualAsync(expected, page.Changes.ToArray());
        await Assert.That(page.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.DeletedSequence);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        await Assert.That(page.CutPosition).IsGreaterThan(PositiveCutBoundary);
    }

    private static Task CurrentAsync(RequestCqrsRf3Callers administrator,
        FeedLiveRf3Scenario scenario, Func<Task> operation, CancellationToken token)
        => FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            FeedLiveRf3NoEffects.Rows(FeedLiveRf3Protocol.DeletedRevision, null),
            FeedLiveRf3Protocol.DeletedSequence, operation, token);
}
