using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Proves current creator/caller authorization and safe projected schedule inspection.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RecurringSchedulePolicyRf3Tests(ClusterFixture fixture)
{
    private const string InspectTool = "keyload_schedule_inspect";

    [Test]
    public async Task AcJobs004RevokedCreatorCannotEmitAndInspectionUsesCurrentProjection()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingRf3Scenario.CreateAsync(fixture, deadline.Token);
        var creator = await RecurringSagaRf3Support.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        var caller = await RecurringSagaRf3Support.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, caller.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            caller.Secret, deadline.Token);
        var id = Guid.NewGuid();
        var definition = RecurringSagaRf3Support.Definition(scenario, id, TimeProvider.System.GetUtcNow().AddMinutes(-1));
        var create = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(definition, 0));
        using var creatorHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var creatorSdk = new KeyLoadClient(creatorHttp, creator.Secret);
        _ = await McpCallerAssertions.SdkSuccessAsync(await creatorSdk.CommitAsync(create, deadline.Token));

        var revokedCreator = MessagingRf3Identity.WithFieldGrants(creator.Principal, [MessagingRf3Scenario.WriteGrant]);
        await MessagingRf3Identity.UpdateAsync(fixture, revokedCreator, deadline.Token);
        var emit = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new EmitRecurringOccurrences(scenario.SourceQueue, id, 1, 1));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, emit, deadline.Token),
            ErrorCode.PermissionDenied, dispatched: true);
        await AssertScheduleUnchangedAsync(scenario, id, deadline.Token);

        var recoveredCreator = MessagingRf3Identity.WithFieldGrants(revokedCreator,
            [MessagingRf3Scenario.WriteGrant, RecurringSagaRf3Support.UseGrant]);
        await MessagingRf3Identity.UpdateAsync(fixture, recoveredCreator, deadline.Token);
        await AssertCallerRevocationAsync(caller, mcp, scenario, emit, id, deadline.Token);
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            emit with { CommandId = Guid.NewGuid() }, deadline.Token));
        await AssertProjectedInspectionAsync(sdk, mcp, scenario, id, caller.Secret, deadline.Token);
    }

    private async Task AssertScheduleUnchangedAsync(MessagingRf3Scenario scenario, Guid id,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var view = await McpCallerAssertions.SdkSuccessAsync(await administrator.InspectRecurringScheduleAsync(
            new(scenario.SourceQueue, id), cancellationToken));
        await Assert.That(view!.NextOrdinal).IsEqualTo(0L);
    }

    private async Task AssertCallerRevocationAsync(MessagingRf3Identity caller, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, CommandRequest emit, Guid scheduleId, CancellationToken cancellationToken)
    {
        var revoked = MessagingRf3Identity.WithCapability(caller.Principal, scenario.SourceQueue,
            Capability.QueuePublish | Capability.QueueInspect);
        await MessagingRf3Identity.UpdateAsync(fixture, revoked, cancellationToken);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit,
            emit with { CommandId = Guid.NewGuid() }, cancellationToken), ErrorCode.PermissionDenied,
            dispatched: true);
        await AssertScheduleUnchangedAsync(scenario, scheduleId, cancellationToken);
        var restored = MessagingRf3Identity.WithCapability(revoked, scenario.SourceQueue,
            Capability.SchedulerManage | Capability.QueuePublish | Capability.QueueInspect);
        await MessagingRf3Identity.UpdateAsync(fixture, restored, cancellationToken);
    }

    private static async Task AssertProjectedInspectionAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingRf3Scenario scenario, Guid id, string credential, CancellationToken cancellationToken)
    {
        var request = new InspectRecurringScheduleRequest(scenario.SourceQueue, id);
        var sdkView = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectRecurringScheduleAsync(request,
            cancellationToken));
        var reply = await mcp.CallAsync(InspectTool, request, cancellationToken);
        var mcpView = await McpCallerAssertions.SuccessAsync<RecurringScheduleInspection?>(reply);
        await Assert.That(sdkView).IsNotNull();
        await Assert.That(sdkView!.Redacted).IsTrue();
        await Assert.That(sdkView.Definition.PayloadJson).IsEqualTo("{}");
        await Assert.That(sdkView.Definition.HeadersJson).IsEqualTo("{}");
        await Assert.That(sdkView.RedactedFields).IsEquivalentTo(
            ["payload:secret", "headers:secret"], CollectionOrdering.Matching);
        await Assert.That(JsonDefaults.Serialize(mcpView.Value)).IsEqualTo(JsonDefaults.Serialize(sdkView));
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, MessagingRf3Scenario.ProtectedPayload);
    }
}
