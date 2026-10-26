namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class ConnectionFixtureConcurrencyFlow
{
    private const string Document = "independent-owner-concurrent-command";
    private const long FirstRevision = 1;

    internal static async Task RunAsync(ConnectionScenario left, ConnectionScenario right)
    {
        var caller = TestContext.Current!.Execution.CancellationToken;
        await Task.WhenAll(left.InitializeAsync(caller), right.InitializeAsync(caller));
        using var deadline = new CancellationTokenSource(left.Timing.CompletionTimeout, TimeProvider.System);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, caller);
        var token = cancellation.Token;
        await Assert.That(left.Fixture.Database.Directory).IsNotEqualTo(right.Fixture.Database.Directory);
        await Assert.That(left.Fixture.ConnectionId).IsNotEqualTo(right.Fixture.ConnectionId);
        await Assert.That(ReferenceEquals(left.Fixture.RequestWork, right.Fixture.RequestWork)).IsFalse();
        var leftPrincipal = left.Principal();
        var rightPrincipal = right.Principal();
        var leftRequest = Guid.NewGuid();
        var rightRequest = Guid.NewGuid();
        var leftCommand = left.Command(Document);
        var rightCommand = right.Command(Document);
        var leftGate = left.Observation.Hold(leftRequest);
        var rightGate = right.Observation.Hold(rightRequest);
        var leftCall = left.CommandAsync(leftPrincipal, leftRequest, leftCommand, token);
        var rightCall = right.CommandAsync(rightPrincipal, rightRequest, rightCommand, token);
        try
        {
            // Both actual execution handlers enter before either original gate is released.
            await Task.WhenAll(leftGate.Arrived.Task, rightGate.Arrived.Task).WaitAsync(token);
            await Assert.That(left.Stored(Document)).IsNull();
            await Assert.That(right.Stored(Document)).IsNull();
            await Assert.That(left.Observation.Read(leftRequest).Identity.PrincipalId).IsEqualTo(leftPrincipal.Id);
            await Assert.That(right.Observation.Read(rightRequest).Identity.PrincipalId).IsEqualTo(rightPrincipal.Id);
            await Assert.That(left.Observation.Read(leftRequest).GrainId)
                .IsNotEqualTo(right.Observation.Read(rightRequest).GrainId);
            await Assert.That(leftCommand.CommandId).IsNotEqualTo(rightCommand.CommandId);
            leftGate.Release.TrySetResult();
            rightGate.Release.TrySetResult();
            var replies = await Task.WhenAll(leftCall, rightCall);
            foreach (var reply in replies)
            {
                await Assert.That(reply.Error).IsNull();
            }
            await Task.WhenAll(left.Observation.Read(leftRequest).Disposed.Task,
                right.Observation.Read(rightRequest).Disposed.Task).WaitAsync(token);
            await Assert.That(left.Stored(Document)!.Revision).IsEqualTo(FirstRevision);
            await Assert.That(right.Stored(Document)!.Revision).IsEqualTo(FirstRevision);
            await Task.WhenAll(left.CloseAsync(token), right.CloseAsync(token));
        }
        finally
        {
            leftGate.Release.TrySetResult();
            rightGate.Release.TrySetResult();
            // Existing scenario disposal cancels/joins retained originals and both owned fixtures.
        }
    }
}
