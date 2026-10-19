using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class ConnectionAwaitedSettlementAssertions
{
    internal static async Task ChargedAsync(RequestWorkOwner owner, Guid requestId)
    {
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
        {
            using var duplicate = owner.Acquire(requestId, RequestWorkKind.RequestProducer);
            return Task.CompletedTask;
        });
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    internal static async Task ReleasedAsync(RequestWorkOwner owner, Guid requestId, CancellationToken token)
    {
        var timing = new RuntimeTestOptions();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var exact = owner.Acquire(requestId, RequestWorkKind.RequestProducer);
                return;
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted)
            {
                await Task.Delay(timing.PollInterval, token);
            }
        }
    }

    internal static async Task SameContextAsync(ConnectionScenario scenario,
        ConnectionAwaitedSettlementObservation observation, Guid original, Guid following)
    {
        await ConnectionAssertions.SameOwnerAsync(scenario, original, following,
            TestContext.Current!.Execution.CancellationToken);
        var first = observation.Identity(original);
        var next = observation.Identity(following);
        await Assert.That(first.ConnectionId).IsEqualTo(scenario.Fixture.ConnectionId);
        await Assert.That(next.ConnectionId).IsEqualTo(first.ConnectionId);
        await Assert.That(first.RequestId).IsNotEqualTo(next.RequestId);
        await Assert.That(first.CommandId).IsNotEqualTo(next.CommandId);
    }

    internal static bool Contains(Exception error, string message)
        => error.Message == message || error is AggregateException aggregate
            && aggregate.InnerExceptions.Any(inner => Contains(inner, message))
            || error.InnerException is { } inner && Contains(inner, message);
}
