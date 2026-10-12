using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ConnectionAwaitedSettlementAssertions
{
    internal static async Task ChargedAsync(NativeRequestWorkOwner owner, Guid requestId)
    {
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
        {
            using var duplicate = owner.Acquire(requestId, NativeRequestWorkKind.RequestProducer);
            return Task.CompletedTask;
        });
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    internal static async Task ReleasedAsync(NativeRequestWorkOwner owner, Guid requestId, CancellationToken token)
    {
        var timing = new NativeRuntimeTestOptions();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var exact = owner.Acquire(requestId, NativeRequestWorkKind.RequestProducer);
                return;
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted)
            { await Task.Delay(timing.PollInterval, token); }
        }
    }

    internal static async Task SameContextAsync(ConnectionNativeScenario scenario,
        ConnectionAwaitedSettlementObservation observation, Guid original, Guid following)
    {
        await ConnectionNativeAssertions.SameOwnerAsync(scenario, original, following,
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
