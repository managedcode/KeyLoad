using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class RequestCqrsProducerNotificationSettlementTests
{
    private const int ExpectedFailureCount = 2;
    private const int PrimaryFailureIndex = 0;
    private const int NotificationFailureIndex = 1;
    [Test]
    public async Task AwaitedOriginalSettlementKeepsExactProducerLeaseUntilNotificationJoins()
    {
        await using var owner = new RequestWorkOwner(Options.Create(new GrainRoutingOptions()));
        var lease = owner.Acquire(Guid.NewGuid(), RequestWorkKind.RequestProducer);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask NotifyAsync()
        {
            arrived.SetResult();
            await release.Task.ConfigureAwait(false);
        }
        var original = RequestWorkStreamSettlement.SettleAndReleaseAwaitedAsync<int>(null, NotifyAsync, null, lease).AsTask();
        try
        {
            await arrived.Task.WaitAsync(TestContext.Current!.Execution.CancellationToken);
            var drain = owner.DrainAsync();
            await Assert.That(ReferenceEquals(drain, owner.DrainAsync())).IsTrue();
            await Assert.That(original.IsCompleted).IsFalse();
            await Assert.That(owner.IsJoined).IsFalse();
            release.SetResult();
            await Assert.That(await original).IsNull();
            await drain;
            await Assert.That(owner.IsJoined).IsTrue();
        }
        finally
        {
            release.TrySetResult();
            await original;
        }
    }

    [Test]
    public async Task OriginalFailureAndAwaitedNotificationFailureRemainExactBeforeLeaseRelease()
    {
        await using var owner = new RequestWorkOwner(Options.Create(new GrainRoutingOptions()));
        var lease = owner.Acquire(Guid.NewGuid(), RequestWorkKind.RequestProducer);
        var primary = new IOException("original-producer");
        var notification = new IOException("original-notification");
        var result = await RequestWorkStreamSettlement.SettleAndReleaseAwaitedAsync<int>(null,
            () => ValueTask.FromException(notification), primary, lease);
        await Assert.That(result is AggregateException).IsTrue();
        var combined = (AggregateException)result!;
        await Assert.That(combined.InnerExceptions.Count).IsEqualTo(ExpectedFailureCount);
        await Assert.That(ReferenceEquals(combined.InnerExceptions[PrimaryFailureIndex], primary)).IsTrue();
        await Assert.That(ReferenceEquals(combined.InnerExceptions[NotificationFailureIndex], notification)).IsTrue();
        await owner.DrainAsync();
        await Assert.That(owner.IsJoined).IsTrue();
    }
}
