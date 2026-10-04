using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueNoQuorumRf3SeedWriter
{
    internal static async Task<DueNoQuorumRf3Seed> CreateAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("due-no-quorum-" + Guid.NewGuid().ToString("N"),
            DueNoQuorumRf3Protocol.Database, DueNoQuorumRf3Protocol.Domain, Guid.NewGuid().ToString("N"));
        var lane = new QueueLaneRef(partition, DueNoQuorumRf3Protocol.Queue);
        var creator = await DueNoQuorumRf3IdentityWriter.CreateAsync(app, partition, lane, profile, cancellationToken)
            .ConfigureAwait(false);
        return await WriteScheduleAsync(app, partition, lane, creator, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DueNoQuorumRf3Seed> WriteScheduleAsync(Aspire.Hosting.DistributedApplication app,
        PartitionRef partition, QueueLaneRef lane, DueNoQuorumRf3Creator creator, CancellationToken cancellationToken)
    {
        var dueAt = TimeProvider.System.GetUtcNow().AddSeconds(DueNoQuorumRf3Protocol.DueDelaySeconds).ToUniversalTime();
        var id = Guid.NewGuid();
        var definition = new RecurringScheduleDefinition(lane, id, dueAt, DueNoQuorumRf3Protocol.Interval,
            DueNoQuorumRf3Protocol.Utc, RecurringMisfirePolicy.CatchUp,
            DueNoQuorumRf3Protocol.Payload, DueNoQuorumRf3Protocol.Headers);
        await using var callers = await DueNoQuorumRf3Callers.ConnectAsync(app, RequestCqrsRf3Protocol.Node1,
            creator.Secret, cancellationToken).ConfigureAwait(false);
        var command = new CommandRequest(Guid.NewGuid(), partition, [new ConfigureRecurringSchedule(definition, 0)]);
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(sdkReceipt).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpReceipt.Value))).IsTrue();
        await Assert.That(sdkReceipt.Mutations).HasSingleItem();
        await Assert.That(sdkReceipt.Mutations[0].Revision).IsEqualTo(1L);
        AssertSetupLead(dueAt);
        return new(partition, lane, id, OccurrenceId(id, 1, 0), OccurrenceId(id, 1, 1), dueAt, definition, creator);
    }

    private static void AssertSetupLead(DateTimeOffset dueAt)
    {
        if (dueAt - TimeProvider.System.GetUtcNow() < TimeSpan.FromSeconds(DueNoQuorumRf3Protocol.MinimumSetupLeadSeconds))
        { throw new TimeoutException(DueNoQuorumRf3Protocol.SetupFailure); }
    }

    private static string OccurrenceId(Guid schedule, long generation, long ordinal)
        => string.Concat("recurring-", schedule.ToString("N", CultureInfo.InvariantCulture), "-",
            generation.ToString("x16", CultureInfo.InvariantCulture), "-",
            ordinal.ToString("x16", CultureInfo.InvariantCulture));
}
