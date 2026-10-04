using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Proves schedule CAS, catch-up identities and cross-client retry on the actual RF3 cluster.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RecurringScheduleRf3Tests(ClusterFixture fixture)
{
    private const int DueMessageCount = 2;

    [Test]
    public async Task AcJobs001And002ConfigureCasCatchUpAndRetryAcrossSdkAndMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await RecurringSagaRf3Support.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var scheduleId = Guid.NewGuid();
        var first = RecurringSagaRf3Support.Definition(scenario, scheduleId, TimeProvider.System.GetUtcNow().AddMinutes(-2));
        var create = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(first, 0));
        var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(create, deadline.Token));
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1L);

        var replacement = RecurringSagaRf3Support.Definition(scenario, scheduleId,
            TimeProvider.System.GetUtcNow().AddMinutes(-1));
        var replace = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(replacement, 1));
        var replaced = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, replace, deadline.Token));
        await Assert.That(replaced.Value.Mutations[0].Revision).IsEqualTo(2L);

        var stale = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(first, 1));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, stale,
            deadline.Token), ErrorCode.RevisionConflict, dispatched: true);
        await RecurringScheduleRf3Assertions.AssertScheduleAsync(sdk, mcp, scenario, scheduleId,
            expectedRevision: 2, expectedGeneration: 2, expectedNextOrdinal: 0, deadline.Token);

        var emit = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new EmitRecurringOccurrences(scenario.SourceQueue, scheduleId, 2, DueMessageCount));
        var emitted = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, emit, deadline.Token));
        var retried = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(emit, deadline.Token));
        await Assert.That(retried).IsEqualTo(emitted.Value);
        await Assert.That(retried.Mutations[0].Revision).IsEqualTo(2L);
        await RecurringScheduleRf3Assertions.AssertScheduleAsync(sdk, mcp, scenario, scheduleId,
            expectedRevision: 2, expectedGeneration: 2, expectedNextOrdinal: DueMessageCount, deadline.Token);
        await RecurringScheduleRf3Assertions.AssertOccurrenceAsync(sdk, scenario, scheduleId, 1,
            replacement.FirstDueAt, deadline.Token);
        await RecurringScheduleRf3Assertions.AssertOccurrenceAsync(sdk, scenario, scheduleId, 2,
            replacement.FirstDueAt.AddSeconds(1), deadline.Token);
        var extra = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(
            new(scenario.SourceQueue,
                RecurringScheduleRf3Assertions.OccurrenceMessageId(scheduleId, generation: 2, ordinal: 3)),
            deadline.Token));
        await Assert.That(extra).IsNull();
    }
}
