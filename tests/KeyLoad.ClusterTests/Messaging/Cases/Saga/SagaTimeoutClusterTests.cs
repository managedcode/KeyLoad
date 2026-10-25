using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

/// <summary>Proves the explicit atomic due-time transition through real public Orleans callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SagaTimeoutClusterTests(ClusterFixture fixture)
{
    private const int DeadlineLeadSeconds = 45;

    [Test]
    public async Task AcJobs003DueTimeoutCommitsOneMessageAndOneTerminalSagaRevision()
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
            MessagingClusterScenario.ProtectedHeaders, TimeToLive: TimeSpan.FromMinutes(1));
        var create = RecurringSagaClusterSupport.Command(Guid.NewGuid(), scenario.SourcePartition,
            new CompareExchangeSaga(scenario.SourceQueue, sagaId, 0, SagaPhase.Waiting,
                MessagingClusterScenario.ProtectedPayload, dueAt, timeout));
        var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(create, deadline.Token));
        await Assert.That(created.Mutations[0].Revision).IsEqualTo(1L);
        await SagaTimeoutClusterAssertions.AssertProjectedAsync(sdk, mcp, scenario, sagaId, dueAt, identity.Secret,
            deadline.Token);

        await Assert.That(TimeProvider.System.GetUtcNow() < dueAt).IsTrue();
        var tooEarly = RecurringSagaClusterSupport.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ExpireSaga(scenario.SourceQueue, sagaId, 1));
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await sdk.CommitAsync(tooEarly, deadline.Token),
            ErrorCode.Validation);
        await SagaTimeoutClusterAssertions.AssertPhaseAsync(sdk, mcp, scenario, sagaId, SagaPhase.Waiting,
            expectedRevision: 1, dueAt, deadline.Token);
        await SagaTimeoutClusterAssertions.WaitUntilAsync(dueAt, deadline.Token);
        await SagaTimeoutClusterAssertions.WaitForTimedOutAsync(sdk, scenario, sagaId, deadline.Token);

        var createReplay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, create, deadline.Token));
        await Assert.That(JsonDefaults.Serialize(createReplay.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(created))).IsTrue();
        var staleTimeout = RecurringSagaClusterSupport.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ExpireSaga(scenario.SourceQueue, sagaId, 1));
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit,
            staleTimeout, deadline.Token), ErrorCode.RevisionConflict, dispatched: true)).Value;
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await sdk.CommitAsync(staleTimeout, deadline.Token),
            ErrorCode.RevisionConflict);
        await SagaTimeoutClusterAssertions.AssertPhaseAsync(sdk, mcp, scenario, sagaId, SagaPhase.TimedOut,
            expectedRevision: 2, dueAt, deadline.Token);
        await SagaTimeoutClusterAssertions.AssertTimeoutMessageAsync(sdk, mcp, scenario, sagaId, dueAt,
            identity.Secret, deadline.Token);
        await SagaTimeoutClusterAssertions.AcknowledgeSingleTimeoutAsync(sdk, mcp, scenario, sagaId, identity.Secret,
            deadline.Token);
        await SagaTimeoutClusterAssertions.AssertNoSecondTransitionAsync(sdk, scenario, sagaId, deadline.Token);
    }
}
