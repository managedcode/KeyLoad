namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionSynchronousExecutionTests
{
    [Test]
    public async Task SynchronousOperationDoesNotBlockAnotherReadOnTheSameConnectionAndPhysicalOwner()
    {
        var heldId = Guid.NewGuid();
        using var observation = new SynchronousRequestObservation(heldId);
        await ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            timeout.CancelAfter(scenario.Timing.CompletionTimeout);
            var principal = scenario.Principal();
            const string documentId = "independent-synchronous-read";
            var created = await scenario.CommandAsync(principal, Guid.NewGuid(), scenario.Command(documentId), token);
            await Assert.That(created.Error).IsNull();
            var held = scenario.ReadAsync(principal, heldId, documentId, token);
            var followingId = Guid.NewGuid();
            try
            {
                await observation.Arrived.Task.WaitAsync(token);
                var following = await scenario.ReadAsync(principal, followingId, documentId, token);
                await Assert.That(following.Error).IsNull();
                await Assert.That(ConnectionAssertions.Value<DocumentResult>(following).Json)
                    .IsEqualTo(ConnectionProtocol.FirstJson);
                await Assert.That(held.IsCompleted).IsFalse();
                await observation.AssertSeparateExecutionAsync(followingId);
            }
            finally
            {
                observation.Release();
            }
            await observation.Exited.Task.WaitAsync(token);
            var released = await held;
            await ConnectionAssertions.SameOwnerAsync(scenario, heldId, followingId, token);
            await Assert.That(released.Error).IsNull();
            await Assert.That(ConnectionAssertions.Value<DocumentResult>(released).Json)
                .IsEqualTo(ConnectionProtocol.FirstJson);
            var healthy = await scenario.ReadAsync(principal, Guid.NewGuid(), documentId, token);
            await Assert.That(healthy.Error).IsNull();
            await scenario.CloseAsync(token);
        }, phaseObserver: observation.Wrap);
    }
}
