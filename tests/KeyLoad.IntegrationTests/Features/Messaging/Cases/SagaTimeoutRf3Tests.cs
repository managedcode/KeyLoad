using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Proves the explicit atomic due-time transition through real public RF3 callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SagaTimeoutRf3Tests(ClusterFixture fixture)
{
    private const int DeadlineLeadSeconds = 45;

    [Test]
    public async Task AcJobs003DueTimeoutCommitsOneMessageAndOneTerminalSagaRevision()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await RecurringSagaRf3Support.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var sagaId = Guid.NewGuid();
        var dueAt = TimeProvider.System.GetUtcNow().AddSeconds(DeadlineLeadSeconds);
        var timeout = new SagaTimeoutDefinition(scenario.TimeoutQueue, MessagingRf3Scenario.SagaTimeoutPayload,
            MessagingRf3Scenario.ProtectedHeaders, TimeToLive: TimeSpan.FromMinutes(1));
        var create = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new CompareExchangeSaga(scenario.SourceQueue, sagaId, 0, SagaPhase.Waiting,
                MessagingRf3Scenario.ProtectedPayload, dueAt, timeout));
        var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(create, deadline.Token));
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1L);
        await SagaTimeoutRf3Assertions.AssertProjectedAsync(sdk, mcp, scenario, sagaId, dueAt, identity.Secret,
            deadline.Token);

        await Assert.That(TimeProvider.System.GetUtcNow() < dueAt).IsTrue();
        var tooEarly = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ExpireSaga(scenario.SourceQueue, sagaId, 1));
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await sdk.CommitAsync(tooEarly, deadline.Token),
            ErrorCode.Validation);
        await SagaTimeoutRf3Assertions.AssertPhaseAsync(sdk, mcp, scenario, sagaId, SagaPhase.Waiting,
            expectedRevision: 1, dueAt, deadline.Token);
        await SagaTimeoutRf3Assertions.WaitUntilAsync(dueAt, deadline.Token);
        await SagaTimeoutRf3Assertions.WaitForTimedOutAsync(sdk, scenario, sagaId, deadline.Token);

        var timeoutCommand = RecurringSagaRf3Support.Command(
            DueCommandRf3Identity.Saga(scenario.SourceQueue, sagaId), scenario.SourcePartition,
            new ExpireSaga(scenario.SourceQueue, sagaId, 1));
        var timeoutReceipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, timeoutCommand, deadline.Token));
        var replay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(timeoutCommand, deadline.Token));
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(timeoutReceipt.Value))).IsTrue();
        await Assert.That(replay.Mutations[0].Revision).IsEqualTo(2L);
        await SagaTimeoutRf3Assertions.AssertPhaseAsync(sdk, mcp, scenario, sagaId, SagaPhase.TimedOut,
            expectedRevision: 2, dueAt, deadline.Token);
        await SagaTimeoutRf3Assertions.AssertTimeoutMessageAsync(sdk, mcp, scenario, sagaId, dueAt,
            identity.Secret, deadline.Token);
        await SagaTimeoutRf3Assertions.AcknowledgeSingleTimeoutAsync(sdk, mcp, scenario, sagaId, identity.Secret,
            deadline.Token);
        await SagaTimeoutRf3Assertions.AssertNoSecondTransitionAsync(sdk, scenario, sagaId, deadline.Token);
    }
}
