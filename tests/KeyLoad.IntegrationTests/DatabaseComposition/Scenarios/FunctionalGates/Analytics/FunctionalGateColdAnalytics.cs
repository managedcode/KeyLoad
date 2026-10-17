using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdAnalytics
{
    internal static async Task<FunctionalGateColdState> CaptureAsync(FunctionalGateFixture fixture,
        FunctionalGateColdState state, CancellationToken token)
    {
        var snapshots = new List<AdminNodeSnapshot>();
        foreach (var node in fixture.NodeNames)
        {
            snapshots.Add(await ReadAsync(fixture, node, token));
        }
        return state with
        {
            Analytics = [.. snapshots]
        };
    }

    internal static async Task VerifyAsync(FunctionalGateFixture fixture, FunctionalGateColdState state, CancellationToken token)
    {
        await Assert.That(state.Analytics.Length).IsEqualTo(fixture.NodeNames.Length);
        for (var index = 0; index < fixture.NodeNames.Length; index++)
        {
            var before = state.Analytics[index];
            var after = await ReadAsync(fixture, fixture.NodeNames[index], token);
            await Assert.That(after.Node.NodeId).IsEqualTo(before.Node.NodeId);
            await Assert.That(after.Node.Incarnation).IsEqualTo(before.Node.Incarnation);
            await Assert.That(after.Http.ProcessInstance).IsNotEqualTo(before.Http.ProcessInstance);
            var original = before.OperationalAnalytics!;
            var current = after.OperationalAnalytics!;
            var sameWindow = current.From == original.From && current.To == original.To;
            if (sameWindow)
            {
                await Assert.That(current.Rows.Single().Samples).IsGreaterThan(original.Rows.Single().Samples);
                await Assert.That(current.Rows.Single().AppliedSum).IsGreaterThanOrEqualTo(original.Rows.Single().AppliedSum);
                await Assert.That(current.Rows.Single().CommittedSum).IsGreaterThanOrEqualTo(original.Rows.Single().CommittedSum);
            }
            // Fixed node names and four timestamps keep this original observation bounded and free of caller data.
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                scenario = "functional-cold-reopen",
                node = fixture.NodeNames[index],
                analyticsHistory = sameWindow ? "OBSERVED_SAME_WINDOW" : "UNQUALIFIED",
                reason = sameWindow ? "SamePublicWindowRowsRetained" : "PublicDashboardWindowChanged",
                originalFrom = original.From,
                originalTo = original.To,
                currentFrom = current.From,
                currentTo = current.To
            }, JsonDefaults.Options));
        }
    }

    private static Task<AdminNodeSnapshot> ReadAsync(FunctionalGateFixture fixture, string node, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, node, fixture.AdminKey, async administrator =>
        {
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.DashboardAsync(token));
            var official = await McpCallerAssertions.SuccessAsync<AdminNodeSnapshot>(
                await administrator.Mcp.CallWithoutBodyAsync(AdminDashboardProtocol.SnapshotTool, token));
            foreach (var snapshot in new[] { sdk, official.Value })
            {
                await Assert.That(snapshot.Node.Voters).IsEqualTo(fixture.NodeNames.Length);
                await Assert.That(snapshot.OperationalAnalytics).IsNotNull();
                var report = snapshot.OperationalAnalytics!;
                await Assert.That(report.NativePlanComplete).IsTrue();
                await Assert.That(report.From.Offset).IsEqualTo(TimeSpan.Zero);
                await Assert.That(report.From.UtcTicks % TimeSpan.TicksPerHour).IsEqualTo(0L);
                await Assert.That(report.To).IsEqualTo(report.From.AddHours(1));
                await Assert.That(report.Rows.Length).IsEqualTo(1);
                await Assert.That(report.Counters.Accepted).IsGreaterThanOrEqualTo(1L);
                await Assert.That(report.Counters.Dropped).IsEqualTo(0L);
                await Assert.That(report.Counters.Rejected).IsEqualTo(0L);
                await Assert.That(report.Counters.Errors).IsEqualTo(0L);
                await Assert.That(report.Counters.Processed).IsEqualTo(report.Counters.Accepted);
                await Assert.That(report.Counters.QueueLength).IsEqualTo(0L);
                var row = report.Rows.Single();
                await Assert.That(row.Hour).IsEqualTo(report.From);
                await Assert.That(row.Samples).IsGreaterThanOrEqualTo(1d);
                await Assert.That(row.AppliedSum).IsGreaterThanOrEqualTo((double)snapshot.Node.Applied);
                await Assert.That(row.CommittedSum - row.AppliedSum).IsEqualTo(row.BacklogSum);
                await Assert.That(new[] { row.Samples, row.AppliedSum, row.CommittedSum, row.BacklogSum }
                    .All(value => double.IsFinite(value) && value >= 0 && value <= 9_007_199_254_740_991
                        && Math.Truncate(value) == value)).IsTrue();
            }
            await Assert.That(official.Value.Node.NodeId).IsEqualTo(sdk.Node.NodeId);
            await Assert.That(official.Value.Node.Incarnation).IsEqualTo(sdk.Node.Incarnation);
            await Assert.That(official.Value.Http.ProcessInstance).IsEqualTo(sdk.Http.ProcessInstance);
            return official.Value;
        }, token);
}
