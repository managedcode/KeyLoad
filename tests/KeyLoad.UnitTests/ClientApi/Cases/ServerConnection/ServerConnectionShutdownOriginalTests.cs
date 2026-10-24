using KeyLoad.Server;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed class ServerConnectionShutdownOriginalTests
{
    private const int OperationTimeoutSeconds = 20;
    private const string OriginalFailure = "Original actual Kestrel connection terminal failure.";

    [Test]
    public async Task CanceledFirstWaitRetainsRemovedTransportOriginalThenHealthyHostServes()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => VerifyFailedOwnerAsync(deadline.Token), failures);
        await ServerFailureObserver.ObserveAsync(() => VerifyHealthyOwnerAsync(deadline.Token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyFailedOwnerAsync(CancellationToken token)
    {
        var original = new InvalidOperationException(OriginalFailure);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = await ConnectionShutdownKestrelOwner.StartAsync(original, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var connection = await owner.ReadConnectionAsync(token);
                await Assert.That(connection).IsNotEqualTo(Guid.Empty);
                using var canceled = new CancellationTokenSource();
                await canceled.CancelAsync();
                var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(() => owner.Registry.ShutdownAsync(canceled.Token));
                await Assert.That(cancellation!.CancellationToken).IsEqualTo(canceled.Token);
                await owner.Terminal.TransportClosed.WaitAsync(token);
                var pending = owner.Registry.ShutdownAsync(CancellationToken.None);
                await Assert.That(pending.IsCompleted).IsFalse();
                owner.Terminal.Release();
                var running = await owner.Terminal.Joined.WaitAsync(token);
                var terminal = await Assert.ThrowsAsync<InvalidOperationException>(() => running);
                await Assert.That(terminal).IsSameReferenceAs(original);
                var afterRemoval = await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Registry.ShutdownAsync(token));
                await Assert.That(afterRemoval).IsSameReferenceAs(original);
                var priorWait = await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(token));
                await Assert.That(priorWait).IsSameReferenceAs(original);
                owner.Terminal.Claim(original);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyHealthyOwnerAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = await ConnectionShutdownKestrelOwner.StartAsync(original: null, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var first = await owner.ReadConnectionAsync(token);
                var second = await owner.ReadConnectionAsync(token);
                await Assert.That(first).IsNotEqualTo(Guid.Empty);
                await Assert.That(second).IsEqualTo(first);
                owner.Terminal.Release();
                await owner.Registry.ShutdownAsync(token);
                await owner.Registry.ShutdownAsync(token);
                var original = await owner.Terminal.Joined.WaitAsync(token);
                await original.WaitAsync(token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
