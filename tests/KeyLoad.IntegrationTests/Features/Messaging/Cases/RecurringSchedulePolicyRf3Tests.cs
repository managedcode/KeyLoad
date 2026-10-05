using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Proves autonomous work rechecks persisted creator and caller authority on real RF3.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RecurringSchedulePolicyRf3Tests(ClusterFixture fixture)
{
    private const string InspectTool = "keyload_schedule_inspect";
    private const int DueLeadSeconds = 45;

    [Test]
    public async Task AcJobs004RevokedCreatorBlocksAutonomousOccurrenceUntilCreatorAndCallerRecover()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingRf3Scenario.CreateAsync(fixture, deadline.Token);
        var creator = await RecurringSagaRf3Support.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        var caller = await RecurringSagaRf3Support.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, caller.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            caller.Secret, deadline.Token);
        using var creatorHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var creatorSdk = new KeyLoadClient(creatorHttp, creator.Secret, IntegrationClientOptions.Execution());
        var id = Guid.NewGuid();
        var firstDueAt = TimeProvider.System.GetUtcNow().AddSeconds(DueLeadSeconds);
        var definition = RecurringSagaRf3Support.Definition(scenario, id, firstDueAt);
        var create = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(definition, 0));
        _ = await McpCallerAssertions.SdkSuccessAsync(await creatorSdk.CommitAsync(create, deadline.Token));
        await AssertScheduleUnchangedAsync(sdk, mcp, scenario, id, firstDueAt, caller.Secret, deadline.Token);

        var revokedCreator = MessagingRf3Identity.WithFieldGrants(creator.Principal,
            [MessagingRf3Scenario.WriteGrant]);
        await MessagingRf3Identity.UpdateAsync(fixture, revokedCreator, deadline.Token);
        await SagaTimeoutRf3Assertions.WaitUntilAsync(firstDueAt, deadline.Token);
        await AssertScheduleUnchangedAsync(sdk, mcp, scenario, id, firstDueAt, caller.Secret, deadline.Token);

        var deniedByCreator = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new EmitRecurringOccurrences(scenario.SourceQueue, id, 1, 1));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit,
            deniedByCreator, deadline.Token), ErrorCode.PermissionDenied, dispatched: true);
        await AssertScheduleUnchangedAsync(sdk, mcp, scenario, id, firstDueAt, caller.Secret, deadline.Token);

        await RevokeCallerManageAsync(fixture, caller, sdk, mcp, scenario, id, firstDueAt, deadline.Token);
        var restoredCreator = MessagingRf3Identity.WithFieldGrants(revokedCreator,
            [MessagingRf3Scenario.WriteGrant, RecurringSagaRf3Support.UseGrant]);
        await MessagingRf3Identity.UpdateAsync(fixture, restoredCreator, deadline.Token);

        await RecurringScheduleRf3Assertions.WaitForOrdinalAsync(sdk, scenario, id, 1, deadline.Token);
        await using var creatorMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            creator.Secret, deadline.Token);
        await CommitAndReplayDueCommandAsync(creatorSdk, creatorMcp, sdk, mcp, scenario, id,
            caller.Secret, firstDueAt, deadline.Token);
    }

    private static async Task RevokeCallerManageAsync(ClusterFixture fixture, MessagingRf3Identity caller,
        KeyLoadClient sdk, McpOfficialClient mcp, MessagingRf3Scenario scenario, Guid id,
        DateTimeOffset firstDueAt, CancellationToken cancellationToken)
    {
        var revoked = MessagingRf3Identity.WithCapability(caller.Principal, scenario.SourceQueue,
            Capability.QueuePublish | Capability.QueueInspect);
        await MessagingRf3Identity.UpdateAsync(fixture, revoked, cancellationToken);
        var denied = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new EmitRecurringOccurrences(scenario.SourceQueue, id, 1, 1));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, denied,
            cancellationToken), ErrorCode.PermissionDenied, dispatched: true);
        await AssertScheduleUnchangedAsync(sdk, mcp, scenario, id, firstDueAt, caller.Secret, cancellationToken);

        var restored = MessagingRf3Identity.WithCapability(revoked, scenario.SourceQueue,
            Capability.SchedulerManage | Capability.QueuePublish | Capability.QueueInspect);
        await MessagingRf3Identity.UpdateAsync(fixture, restored, cancellationToken);
    }

    private static async Task CommitAndReplayDueCommandAsync(KeyLoadClient creatorSdk, McpOfficialClient creatorMcp,
        KeyLoadClient sdk, McpOfficialClient mcp, MessagingRf3Scenario scenario, Guid scheduleId,
        string credential, DateTimeOffset firstDueAt,
        CancellationToken cancellationToken)
    {
        var commandId = DueCommandRf3Identity.Schedule(scenario.SourceQueue, scheduleId);
        var command = RecurringSagaRf3Support.Command(commandId, scenario.SourcePartition,
            new EmitRecurringOccurrences(scenario.SourceQueue, scheduleId, 1, 1));
        var sdkReceipt = await McpCallerAssertions.SdkSuccessAsync(await creatorSdk.CommitAsync(command, cancellationToken));
        var mcpReply = await creatorMcp.CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken);
        var mcpReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(mcpReply);
        await Assert.That(sdkReceipt.Mutations).HasSingleItem();
        await Assert.That(sdkReceipt.Mutations[0].Kind).IsEqualTo("emitRecurringOccurrences");
        await Assert.That(sdkReceipt.Mutations[0].Revision).IsEqualTo(1L);
        await Assert.That(JsonDefaults.Serialize(mcpReceipt.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkReceipt))).IsTrue();
        await AssertProjectedInspectionAsync(sdk, mcp, scenario, scheduleId, credential, firstDueAt,
            cancellationToken);
        await RecurringScheduleRf3Assertions.AssertOccurrenceAsync(sdk, mcp, scenario, scheduleId,
            generation: 1, ordinal: 0, firstDueAt, credential, cancellationToken);
        await RecurringScheduleRf3Assertions.AssertOccurrenceAbsentAsync(sdk, mcp, scenario, scheduleId,
            generation: 1, ordinal: 1, cancellationToken);
    }

    private static async Task AssertScheduleUnchangedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid id, DateTimeOffset firstDueAt, string credential,
        CancellationToken cancellationToken)
    {
        await RecurringScheduleRf3Assertions.AssertScheduleAsync(sdk, mcp, scenario, id,
            expectedRevision: 1, expectedGeneration: 1, expectedNextOrdinal: 0, firstDueAt, credential,
            cancellationToken);
        await RecurringScheduleRf3Assertions.AssertOccurrenceAbsentAsync(sdk, mcp, scenario, id,
            generation: 1, ordinal: 0, cancellationToken);
    }

    private static async Task AssertProjectedInspectionAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid id, string credential, DateTimeOffset firstDueAt,
        CancellationToken cancellationToken)
    {
        var request = new InspectRecurringScheduleRequest(scenario.SourceQueue, id);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(request,
            cancellationToken));
        var reply = await mcp.CallAsync(InspectTool, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<RecurringScheduleInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(mcpView.Value).IsNotNull();
        await Assert.That(sdkView!.Revision).IsEqualTo(1L);
        await Assert.That(sdkView.Generation).IsEqualTo(1L);
        await Assert.That(sdkView.NextOrdinal).IsEqualTo(1L);
        await Assert.That(sdkView.Definition.FirstDueAt).IsEqualTo(firstDueAt);
        await Assert.That(sdkView.Redacted).IsTrue();
        await Assert.That(sdkView.Definition.PayloadJson).IsEqualTo("{}");
        await Assert.That(sdkView.Definition.HeadersJson).IsEqualTo("{}");
        await Assert.That(sdkView.RedactedFields).IsEquivalentTo(
            ["payload:/secret", "headers:/secret"], CollectionOrdering.Matching);
        await Assert.That(JsonDefaults.Serialize(mcpView.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(sdkView))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedPayload);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedHeaders);
    }
}
