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
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var scheduleId = Guid.NewGuid();
        var firstDueAt = TimeProvider.System.GetUtcNow().AddMinutes(5);
        var first = RecurringSagaRf3Support.Definition(scenario, scheduleId, firstDueAt);
        var create = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(first, 0));
        var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(create, deadline.Token));
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1L);
        await RecurringScheduleRf3Assertions.AssertScheduleAsync(sdk, mcp, scenario, scheduleId,
            expectedRevision: 1, expectedGeneration: 1, expectedNextOrdinal: 0, firstDueAt,
            identity.Secret, deadline.Token);

        var replacementFirstDueAt = TimeProvider.System.GetUtcNow().AddHours(-1).AddMinutes(-5);
        var replacement = RecurringSagaRf3Support.Definition(scenario, scheduleId, replacementFirstDueAt);
        var replace = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(replacement, 1));
        var replaced = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, replace, deadline.Token));
        await Assert.That(replaced.Value.Mutations[0].Revision).IsEqualTo(2L);

        var stale = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(first, 1));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, stale,
            deadline.Token), ErrorCode.RevisionConflict, dispatched: true);
        await Assert.That(replacementFirstDueAt.AddHours(1)).IsLessThan(TimeProvider.System.GetUtcNow());
        await Assert.That(replacementFirstDueAt.AddHours(2)).IsGreaterThan(TimeProvider.System.GetUtcNow());

        await EmitRetryAndVerifyAsync(sdk, mcp, scenario, scheduleId, replacementFirstDueAt,
            identity.Secret, deadline.Token);
    }

    private static async Task EmitRetryAndVerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid scheduleId, DateTimeOffset firstDueAt, string credential,
        CancellationToken cancellationToken)
    {
        var emit = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new EmitRecurringOccurrences(scenario.SourceQueue, scheduleId, 2, DueMessageCount));
        var emitted = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, emit, cancellationToken));
        var retried = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(emit, cancellationToken));
        await Assert.That(JsonDefaults.Serialize(retried).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(emitted.Value))).IsTrue();
        await Assert.That(retried.Mutations[0].Revision).IsEqualTo(2L);
        await RecurringScheduleRf3Assertions.AssertScheduleAsync(sdk, mcp, scenario, scheduleId,
            expectedRevision: 2, expectedGeneration: 2, expectedNextOrdinal: DueMessageCount,
            firstDueAt, credential, cancellationToken);
        await RecurringScheduleRf3Assertions.AssertOccurrenceAsync(sdk, mcp, scenario, scheduleId,
            generation: 2, ordinal: 0, firstDueAt, credential, cancellationToken);
        await RecurringScheduleRf3Assertions.AssertOccurrenceAsync(sdk, mcp, scenario, scheduleId,
            generation: 2, ordinal: 1, firstDueAt.AddHours(1), credential, cancellationToken);
        await RecurringScheduleRf3Assertions.AssertOccurrenceAbsentAsync(sdk, mcp, scenario, scheduleId,
            generation: 2, ordinal: 2, cancellationToken);
    }
}
