using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueFaultRf3SeedWriter
{
    internal static async Task<DueFaultRf3Seed> CreateAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(DueFaultRf3Protocol.QualificationPrefix + Guid.NewGuid().ToString("N"),
            DueFaultRf3Protocol.Database, DueFaultRf3Protocol.Domain, Guid.NewGuid().ToString("N"));
        var recurringLane = new QueueLaneRef(partition, DueFaultRf3Protocol.RecurringQueue);
        var sagaLane = new QueueLaneRef(partition, DueFaultRf3Protocol.SagaQueue);
        var timeoutLane = new QueueLaneRef(partition, DueFaultRf3Protocol.TimeoutQueue);
        var creator = await DueFaultRf3IdentityWriter.CreateAsync(app, partition, recurringLane, sagaLane, timeoutLane,
            profile, cancellationToken).ConfigureAwait(false);
        await using var callers = await NodeEpochRf3Callers.ConnectAsync(app,
            RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2,
            creator.Secret, cancellationToken).ConfigureAwait(false);
        return await WriteDueRecordsAsync(callers, partition, recurringLane, sagaLane, timeoutLane, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<DueFaultRf3Seed> WriteDueRecordsAsync(NodeEpochRf3Callers callers,
        PartitionRef partition, QueueLaneRef recurringLane, QueueLaneRef sagaLane, QueueLaneRef timeoutLane,
        CancellationToken cancellationToken)
    {
        var dueAt = TimeProvider.System.GetUtcNow().AddSeconds(DueFaultRf3Protocol.DueDelaySeconds).ToUniversalTime();
        var scheduleId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var schedule = new RecurringScheduleDefinition(recurringLane, scheduleId, dueAt,
            DueFaultRf3Protocol.RecurrenceInterval, "UTC", RecurringMisfirePolicy.CatchUp,
            DueFaultRf3Protocol.RecurringPayload, DueFaultRf3Protocol.RecurringHeaders);
        var scheduleCommand = Command(partition, new ConfigureRecurringSchedule(schedule, 0));
        var scheduleReceipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(scheduleCommand,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(scheduleReceipt.Mutations).HasSingleItem();
        await Assert.That(scheduleReceipt.Mutations[0].Revision).IsEqualTo(1L);
        var sagaCommand = SagaCommand(partition, sagaLane, timeoutLane, sagaId, dueAt);
        var sagaReply = await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, sagaCommand, cancellationToken)
            .ConfigureAwait(false);
        var sagaReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(sagaReply).ConfigureAwait(false);
        await Assert.That(sagaReceipt.Value.Mutations).HasSingleItem();
        await Assert.That(sagaReceipt.Value.Mutations[0].Revision).IsEqualTo(1L);
        AssertSetupLead(dueAt);
        return new(partition, recurringLane, sagaLane, timeoutLane, scheduleId, sagaId,
            DueFaultRf3Identifiers.Occurrence(scheduleId), DueFaultRf3Identifiers.TimeoutMessage(sagaId), dueAt,
            schedule, scheduleCommand, sagaCommand);
    }

    private static CommandRequest SagaCommand(PartitionRef partition, QueueLaneRef sagaLane,
        QueueLaneRef timeoutLane, Guid sagaId, DateTimeOffset dueAt)
    {
        var timeout = new SagaTimeoutDefinition(timeoutLane, DueFaultRf3Protocol.TimeoutPayload,
            DueFaultRf3Protocol.TimeoutHeaders, TimeToLive: DueFaultRf3Protocol.TimeoutTtl);
        return Command(partition, new CompareExchangeSaga(sagaLane, sagaId, 0, SagaPhase.Waiting,
            "{}", dueAt, timeout));
    }

    private static CommandRequest Command(PartitionRef partition, Mutation mutation)
        => new(Guid.NewGuid(), partition, [mutation]);

    internal static void AssertSetupLead(DateTimeOffset dueAt)
    {
        if (dueAt - TimeProvider.System.GetUtcNow() < TimeSpan.FromSeconds(DueFaultRf3Protocol.MinimumSetupLeadSeconds))
        { throw new TimeoutException(DueFaultRf3Protocol.SetupLeadFailure); }
    }
}
