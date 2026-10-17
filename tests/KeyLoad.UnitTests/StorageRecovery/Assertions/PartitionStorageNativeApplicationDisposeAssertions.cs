using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeApplicationDisposeAssertions
{
    private const int RecurringIndex = 0;
    private const int ChunksIndex = 1;
    private const int AnnIndex = 2;
    private const int ProducerCount = 3;

    internal static async Task ClosedAsync(PartitionStorageNativeApplicationDisposeCapture captured,
        Task? originalDispose)
    {
        await Assert.That(originalDispose?.IsCompletedSuccessfully == true).IsTrue();
        await Assert.That(captured.Owner.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(captured.Node.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(captured.Node.HasJoinedRequestWork).IsTrue();
        await Assert.That(captured.Owner.CanReleaseRoot).IsTrue();
        var originalJoins = captured.Owner.StorageProducerJoins;
        await Assert.That(originalJoins is not null).IsTrue();
        await Assert.That(originalJoins!.Count).IsEqualTo(ProducerCount);
        // Observe only AFTER the actual owner close; these calls must not pre-fence startup.
        await Assert.That(ReferenceEquals(originalJoins[RecurringIndex], captured.Recurring.JoinStorageProducerAsync())).IsTrue();
        await Assert.That(ReferenceEquals(originalJoins[ChunksIndex], captured.Chunks.JoinStorageProducerAsync())).IsTrue();
        await Assert.That(ReferenceEquals(originalJoins[AnnIndex], captured.Ann.JoinStorageProducerAsync())).IsTrue();
        foreach (var original in originalJoins)
        {
            await Assert.That(original.IsCompletedSuccessfully).IsTrue();
        }
    }

    internal static void RequireReleased(string directory, List<Exception> failures)
    {
        FileStream? original = null;
        ServerFailureObserver.Observe(() => original = new FileStream(
            Path.Combine(directory, PartitionStoreProtocol.OwnershipFile), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None), failures);
        if (original is not null)
        {
            ServerFailureObserver.Observe(original.Dispose, failures);
        }
    }
}
