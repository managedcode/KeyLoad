using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionSettlementTests
{
    [Test]
    public Task AcCrs056NeverPulledStreamDoesNotActivateAndEarlyDisposalJoinsWithoutEffects()
    {
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            timeout.CancelAfter(scenario.Timing.CompletionTimeout);
            var principal = scenario.Principal();
            var requestId = Guid.NewGuid();
            var command = scenario.Command("disposed-original");
            var envelope = scenario.Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch,
                command.CommandId, command);
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
