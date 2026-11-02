namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionRequestLifetimeTests
{
    [Test]
    public Task AcCrs051ActiveRequestOutlivesConnectionIdleTimeoutUntilCallerCancels()
        => ConnectionScenario.RunAsync(async scenario =>
        {
            using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var token = lifetime.Token;
            await scenario.InitializeAsync(token);
            var principal = scenario.Principal();
            var firstId = Guid.NewGuid();
            var held = scenario.Observation.Hold(firstId);
            var command = scenario.Command("caller-owned-lifetime");
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
            var first = scenario.CommandAsync(principal, firstId, command, caller.Token);
            try
            {
                await held.Arrived.Task.WaitAsync(token);
                await Task.Delay(TimeSpan.FromSeconds(125), token);
                await Assert.That(first.IsCompleted).IsFalse();
                await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command,
                    "caller-owned-lifetime");
                var followingId = Guid.NewGuid();
                var following = scenario.Command("healthy-while-active");
                var reply = await scenario.CommandAsync(principal, followingId, following, token);
                await Assert.That(reply.Error).IsNull();
                await ConnectionAssertions.StoredAsync(scenario, "healthy-while-active", 1, ConnectionProtocol.FirstJson);
                await caller.CancelAsync();
                await ConnectionAssertions.CancelledAsync(first);
                await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command,
                    "caller-owned-lifetime");
                await ConnectionAssertions.SameOwnerAsync(scenario, firstId, followingId, token);
                await scenario.CloseAsync(token);
            }
            finally
            {
                await caller.CancelAsync();
                held.Release.TrySetResult();
            }
        });
}
