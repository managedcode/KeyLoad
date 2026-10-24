using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

/// <summary>Proves autonomous saga timeout commits once and remains inspectable through SDK and MCP.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SagaDueClusterTests(ClusterFixture fixture)
{
    private const int DeadlineLeadSeconds = 30;

    [Test]
    public async Task AcDue003WaitingSagaTimesOutAutonomouslyAndLeavesNoSecondDelivery()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await RecurringSagaClusterSupport.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var sagaId = Guid.NewGuid();
        var dueAt = TimeProvider.System.GetUtcNow().AddSeconds(DeadlineLeadSeconds);
        var timeout = new SagaTimeoutDefinition(scenario.TimeoutQueue, MessagingClusterScenario.SagaTimeoutPayload,
            MessagingClusterScenario.ProtectedHeaders, TimeToLive: TimeSpan.FromSeconds(DueSagaClusterAssertions.TimeoutSeconds));
        var command = RecurringSagaClusterSupport.Command(Guid.NewGuid(), scenario.SourcePartition,
            new CompareExchangeSaga(scenario.SourceQueue, sagaId, 0, SagaPhase.Waiting,
                MessagingClusterScenario.ProtectedPayload, dueAt, timeout));
        var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token));
        await Assert.That(created.Mutations).HasSingleItem();
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1L);
        await RuntimeConstructorCycleClusterReceipt.CreatedAsync(fixture, sdk, mcp, command, created, deadline.Token);
        await DueSagaClusterAssertions.AssertSagaAsync(sdk, mcp, scenario, sagaId,
            SagaPhase.Waiting, 1, dueAt, identity.Secret, deadline.Token);
        await DueSagaClusterAssertions.AssertTimeoutAbsentAsync(sdk, mcp, scenario, sagaId, deadline.Token);

        await DueRecurringClusterAssertions.WaitForDueTimeAsync(dueAt, deadline.Token);
        await DueSagaClusterAssertions.WaitForTimedOutAsync(sdk, scenario, sagaId, deadline.Token);
        await DueSagaClusterAssertions.AssertSagaAsync(sdk, mcp, scenario, sagaId,
            SagaPhase.TimedOut, 2, dueAt, identity.Secret, deadline.Token);
        await DueSagaClusterAssertions.AssertTimeoutAsync(sdk, mcp, scenario, sagaId, dueAt, identity.Secret,
            deadline.Token);
        await RuntimeConstructorCycleClusterRefusal.RunAsync(fixture, sdk, mcp, scenario, sagaId, dueAt,
            command, created, deadline.Token);
        await DueSagaClusterAssertions.ConsumeAndAcknowledgeExactlyOneAsync(sdk, mcp, scenario, sagaId, identity.Secret,
            deadline.Token);
        await RuntimeConstructorCycleClusterReceipt.ReplayAsync(sdk, mcp, command, created, deadline.Token);
        await DueSagaClusterAssertions.AssertSagaAsync(sdk, mcp, scenario, sagaId,
            SagaPhase.TimedOut, 2, dueAt, identity.Secret, deadline.Token);
    }
}
