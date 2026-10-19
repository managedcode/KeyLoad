using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionHostApplicationConcurrency
{
    internal static async Task<PartitionHost> OpenAsync(PartitionHostApplicationOwner owner, IGrainContext context)
    {
        await Assert.That(owner.Opening is null).IsTrue();
        Assert.ThrowsExactly<InvalidOperationException>(() => owner.Borrow());
        var openings = new Task[PartitionHostApplicationProtocol.Callers];
        Parallel.For(PartitionHostApplicationProtocol.FirstCaller, openings.Length, index => openings[index] = owner.OpenAsync(context));
        var original = openings[PartitionHostApplicationProtocol.FailureIndex];
        foreach (var opening in openings)
        {
            await Assert.That(ReferenceEquals(opening, original)).IsTrue();
        }
        await original.ConfigureAwait(false);
        await Assert.That(original.IsCompletedSuccessfully).IsTrue();
        await Assert.That(ReferenceEquals(owner.Opening, original)).IsTrue();
        var borrowed = new PartitionHost[PartitionHostApplicationProtocol.Callers];
        Parallel.For(PartitionHostApplicationProtocol.FirstCaller, borrowed.Length, index => borrowed[index] = owner.Borrow());
        var host = borrowed[PartitionHostApplicationProtocol.FailureIndex];
        foreach (var candidate in borrowed)
        {
            await Assert.That(ReferenceEquals(candidate, host)).IsTrue();
        }
        return host;
    }

    internal static async Task CloseAsync(PartitionHostApplicationOwner owner, IGrainContext context)
    {
        var closing = new Task[PartitionHostApplicationProtocol.Callers];
        Parallel.For(PartitionHostApplicationProtocol.FirstCaller, closing.Length, index => closing[index] = owner.DisposeAsync().AsTask());
        var original = closing[PartitionHostApplicationProtocol.FailureIndex];
        foreach (var candidate in closing)
        {
            await Assert.That(ReferenceEquals(candidate, original)).IsTrue();
        }
        await original.ConfigureAwait(false);
        await Assert.That(original.IsCompletedSuccessfully).IsTrue();
        await Assert.That(owner.CanReleaseRoot).IsTrue();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.Borrow());
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.OpenAsync(context));
        await Assert.That(ReferenceEquals(original, owner.DisposeAsync().AsTask())).IsTrue();
    }
}
