using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Proves autonomous saga timeout commits once and remains inspectable through SDK and MCP.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SagaDueRf3Tests(ClusterFixture fixture)
{
    private const int DeadlineLeadSeconds = 30;

    [Test]
    public async Task AcDue003WaitingSagaTimesOutAutonomouslyAndLeavesNoSecondDelivery()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingRf3Scenario.CreateAsync(fixture, deadline.Token);
        var identity = await RecurringSagaRf3Support.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var sagaId = Guid.NewGuid();
        var dueAt = TimeProvider.System.GetUtcNow().AddSeconds(DeadlineLeadSeconds);
        var timeout = new SagaTimeoutDefinition(scenario.TimeoutQueue, MessagingRf3Scenario.SagaTimeoutPayload,
            MessagingRf3Scenario.ProtectedHeaders, TimeToLive: TimeSpan.FromSeconds(DueSagaRf3Assertions.TimeoutSeconds));
        var command = RecurringSagaRf3Support.Command(Guid.NewGuid(), scenario.SourcePartition,
            new CompareExchangeSaga(scenario.SourceQueue, sagaId, 0, SagaPhase.Waiting,
                MessagingRf3Scenario.ProtectedPayload, dueAt, timeout));
        var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token));
        await Assert.That(created.Mutations).HasSingleItem();
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1L);
        await DueSagaRf3Assertions.AssertSagaAsync(sdk, mcp, scenario, sagaId,
            SagaPhase.Waiting, 1, dueAt, identity.Secret, deadline.Token);
        await DueSagaRf3Assertions.AssertTimeoutAbsentAsync(sdk, mcp, scenario, sagaId, deadline.Token);

        await DueRecurringRf3Assertions.WaitForDueTimeAsync(dueAt, deadline.Token);
        await DueSagaRf3Assertions.WaitForTimedOutAsync(sdk, scenario, sagaId, deadline.Token);
        await DueSagaRf3Assertions.AssertSagaAsync(sdk, mcp, scenario, sagaId,
            SagaPhase.TimedOut, 2, dueAt, identity.Secret, deadline.Token);
        await DueSagaRf3Assertions.AssertTimeoutAsync(sdk, mcp, scenario, sagaId, dueAt, identity.Secret,
            deadline.Token);
        await DueSagaRf3Assertions.ConsumeAndAcknowledgeExactlyOneAsync(sdk, mcp, scenario, sagaId, identity.Secret,
            deadline.Token);
        await DueSagaRf3Assertions.AssertSagaAsync(sdk, mcp, scenario, sagaId,
            SagaPhase.TimedOut, 2, dueAt, identity.Secret, deadline.Token);
    }
}
