using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageApplicationDisposeAssertions
{
    internal static async Task ClosedAsync(PartitionStorageApplicationDisposeCapture captured,
        Task? originalDispose)
    {
        await Assert.That(originalDispose?.IsCompletedSuccessfully == true).IsTrue();
        await Assert.That(captured.Owner.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(captured.Node.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(captured.Node.RequestWork.IsJoined).IsTrue();
        await Assert.That(captured.Node.HasJoinedRequestWork).IsTrue();
        await Assert.That(captured.Owner.CanReleaseRoot).IsTrue();
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
