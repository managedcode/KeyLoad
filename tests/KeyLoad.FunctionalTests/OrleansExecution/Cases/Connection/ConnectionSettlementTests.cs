using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionSettlementTests
{
    [Test]
    public Task AcCrs055ExecutionDeadlineJoinsSelectedOperationAndFollowingWriteSucceeds()
    {
        var timing = new RuntimeTestOptions();
        var routing = new GrainRoutingOptions();
        return ConnectionScenario.RunAsync(async scenario =>
        {
            await scenario.InitializeAsync(TestContext.Current!.Execution.CancellationToken);
            using var timeout = new CancellationTokenSource(timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            var principal = scenario.Principal();
            routing.ExecutionLifetime = timing.HeldJobDelay;
            var requestId = Guid.NewGuid();
            var held = scenario.Observation.Hold(requestId);
            var command = scenario.Command("deadline-original");
            var original = scenario.CommandAsync(principal, requestId, command, token);
            await held.Arrived.Task.WaitAsync(token);
            await ConnectionAssertions.CancelledAsync(original);
            await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
            await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command, "deadline-original");
            var followingId = Guid.NewGuid();
            var following = await scenario.CommandAsync(principal, followingId, scenario.Command("deadline-following"), token);
            await Assert.That(following.Error).IsNull();
            await ConnectionAssertions.StoredAsync(scenario, "deadline-following", 1, ConnectionProtocol.FirstJson);
            await ConnectionAssertions.SameOwnerAsync(scenario, requestId, followingId, token);
            await scenario.CloseAsync(token);
        }, routing);
    }

    [Test]
    public Task AcCrs056NeverPulledStreamDoesNotActivateAndEarlyDisposalJoinsWithoutEffects()
    {
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            var principal = scenario.Principal();
            var requestId = Guid.NewGuid();
            var command = scenario.Command("disposed-original");
            var envelope = scenario.Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch,
                command.CommandId, OrleansSerialization.Serialize(command));
            var held = scenario.Observation.Hold(requestId);
            using (new GrainRequestIdentityScope(scenario.Fixture.Cluster.ServiceProvider, principal,
                requestId, command.CommandId, token, connectionId: scenario.Fixture.ConnectionId))
            {
                var stream = scenario.Connection.ExecuteStreamAsync(envelope, token)
                    .WithBatchSize(GrainRequestStreamProtocol.BatchSize);
                await scenario.AssertActivationCountAsync(ConnectionProtocol.AbsentActivations, token);
                await using var iterator = stream.GetAsyncEnumerator(token);
                await Assert.That(await iterator.MoveNextAsync()).IsTrue();
                await Assert.That(iterator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
                await Assert.That(iterator.Current.ProgressResult?.Value?.RequestId).IsEqualTo(requestId);
                await held.Arrived.Task.WaitAsync(token);
            }
            await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
            await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command, "disposed-original");
            var followingId = Guid.NewGuid();
            var following = await scenario.CommandAsync(principal, followingId, scenario.Command("disposed-following"), token);
            await Assert.That(following.Error).IsNull();
            await ConnectionAssertions.StoredAsync(scenario, "disposed-following", 1, ConnectionProtocol.FirstJson);
            await ConnectionAssertions.SameOwnerAsync(scenario, requestId, followingId, token);
            await scenario.CloseAsync(token);
        });
    }
}
