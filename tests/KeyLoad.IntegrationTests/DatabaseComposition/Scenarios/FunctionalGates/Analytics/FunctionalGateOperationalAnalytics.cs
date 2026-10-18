using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Verifies real public native reports without treating derived history as canonical data.</summary>
internal static class FunctionalGateOperationalAnalytics
{
    private const int Empty = 0;
    private const long Created = 1;
    private const long Deleted = 2;
    private const string Json = "{\"kind\":\"operational-analytics-sentinel\"}";
    private const string PrivateSentinel = "operational-analytics-sentinel";
    private const string PutKind = "putDocument";
    private const string DeleteKind = "deleteDocument";
    private const int MaximumBytes = 65_536;
    private const double MaximumExactInteger = 9_007_199_254_740_991;
    private const int SingleHour = 1;
    private const int MinimumSamples = 2;

    internal static async Task ExerciseAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        var reference = ctx.Bind(new(ctx.Partition, ctx.MutableDocumentCollection, "native-analytics-" + Guid.NewGuid().ToString("N")));
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition,
            [new PutDocument(reference.Collection, reference.Id, Json, Empty)]);
        var receipt = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
        await SqlClusterProtocol.EqualAsync(receipt, await ctx.McpAsync<CommitReceipt>(ctx.Writer,
            McpCallerTools.DocumentsCommit, command, token));
        await SqlClusterProtocol.EqualAsync(new[] { new MutationReceipt(PutKind, reference.Collection, reference.Id, Created) },
            receipt.Mutations.ToArray());
        await DocumentAsync(ctx, reference, token);
        var first = await ctx.SdkAsync(ctx.Administrator.Sdk.DashboardAsync(token));
        var second = await ctx.McpWithoutBodyAsync<AdminNodeSnapshot>(ctx.Administrator,
            AdminDashboardProtocol.SnapshotTool, token);
        await ReportAsync(first, ctx.VoterCount);
        await ReportAsync(second, ctx.VoterCount);
        await ConsecutiveAsync(first, second);
        var denied = await ctx.Reader.Sdk.DashboardAsync(token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await SqlClusterProtocol.EqualAsync(receipt, await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token)));
        await SqlClusterProtocol.EqualAsync(receipt, await ctx.McpAsync<CommitReceipt>(ctx.Writer,
            McpCallerTools.DocumentsCommit, command, token));
        await DocumentAsync(ctx, reference, token);
        var cleanup = new CommandRequest(Guid.NewGuid(), ctx.Partition,
            [new DeleteDocument(reference.Collection, reference.Id, Created)]);
        var removed = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(cleanup, token));
        await SqlClusterProtocol.EqualAsync(new[] { new MutationReceipt(DeleteKind, reference.Collection, reference.Id, Deleted) },
            removed.Mutations.ToArray());
        await SqlClusterProtocol.EqualAsync(removed, await ctx.McpAsync<CommitReceipt>(ctx.Writer,
            McpCallerTools.DocumentsCommit, cleanup, token));
    }

    private static async Task DocumentAsync(FunctionalGateContext ctx, EntityRef reference, CancellationToken token)
    {
        var expected = new DocumentReadResult(new(reference, Created, Json, false, []));
        await SqlClusterProtocol.EqualAsync(expected, await ctx.SdkAsync(ctx.Reader.Sdk.GetAsync(reference, token)));
        await SqlClusterProtocol.EqualAsync(expected, await ctx.McpAsync<DocumentReadResult>(ctx.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
    }

    private static async Task ReportAsync(AdminNodeSnapshot snapshot, int voters)
    {
        await Assert.That(snapshot.Node.Voters).IsEqualTo(voters);
        await Assert.That(snapshot.Node.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(snapshot.OperationalAnalytics).IsNotNull();
        var report = snapshot.OperationalAnalytics!;
        await Assert.That(report.From.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(report.From.UtcTicks % TimeSpan.TicksPerHour).IsEqualTo((long)Empty);
        await Assert.That(report.To).IsEqualTo(report.From.AddHours(SingleHour));
        await Assert.That(report.NativePlanComplete).IsTrue();
        await Assert.That(report.Rows.Length).IsEqualTo(SingleHour);
        await Assert.That(report.Counters.Accepted).IsGreaterThanOrEqualTo((long)MinimumSamples);
        await Assert.That(report.Counters.Processed).IsEqualTo(report.Counters.Accepted);
        await Assert.That(report.Counters.Dropped).IsEqualTo((long)Empty);
        await Assert.That(report.Counters.Rejected).IsEqualTo((long)Empty);
        await Assert.That(report.Counters.Errors).IsEqualTo((long)Empty);
        await Assert.That(report.Counters.QueueLength).IsEqualTo((long)Empty);
        var row = report.Rows.Single();
        await Assert.That(row.Hour).IsEqualTo(report.From);
        await Assert.That(row.Samples).IsGreaterThanOrEqualTo((double)SingleHour);
        await Assert.That(row.AppliedSum).IsGreaterThanOrEqualTo((double)snapshot.Node.Applied);
        await Assert.That(row.CommittedSum - row.AppliedSum).IsEqualTo(row.BacklogSum);
        await Assert.That(new[] { row.Samples, row.CommittedSum, row.AppliedSum, row.BacklogSum }.All(value =>
            double.IsFinite(value) && value >= Empty && value <= MaximumExactInteger && Math.Truncate(value) == value)).IsTrue();
        var bytes = JsonDefaults.Serialize(report);
        await Assert.That(bytes.Length).IsLessThanOrEqualTo(MaximumBytes);
        await Assert.That(System.Text.Encoding.UTF8.GetString(bytes).Contains(PrivateSentinel, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task ConsecutiveAsync(AdminNodeSnapshot first, AdminNodeSnapshot second)
    {
        if (first.Node.NodeId == second.Node.NodeId && first.Node.Incarnation == second.Node.Incarnation
            && first.Http.ProcessInstance == second.Http.ProcessInstance)
        {
            var before = first.OperationalAnalytics!;
            var after = second.OperationalAnalytics!;
            await Assert.That(after.Counters.Accepted).IsEqualTo(before.Counters.Accepted + SingleHour);
            if (before.From == after.From)
            {
                await Assert.That(after.Rows.Single().Samples).IsEqualTo(before.Rows.Single().Samples + SingleHour);
            }
        }
    }
}
