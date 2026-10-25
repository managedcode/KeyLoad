using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionHostApplicationLock
{
    internal static async Task RefuseAsync(PartitionHostApplicationSession session, string directory)
    {
        var original = session.Owner.OpenAsync(session.Context);
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, observed).ConfigureAwait(false);
        if (observed.Count != PartitionHostApplicationProtocol.OneFailure ||
            observed[PartitionHostApplicationProtocol.FailureIndex] is not IOException)
        {
            ServerFailureObserver.ThrowIfAny(observed);
            throw new InvalidOperationException(PartitionHostApplicationProtocol.MissingFailure);
        }
        session.ExpectedOpeningFailure = observed[PartitionHostApplicationProtocol.FailureIndex];
        await Assert.That(original.IsFaulted).IsTrue();
        await Assert.That(ReferenceEquals(original, session.Owner.OpenAsync(session.Context))).IsTrue();
        await Assert.That(ReferenceEquals(original, session.Owner.Opening)).IsTrue();
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Owner.Borrow());
        await Assert.That(session.Owner.CanReleaseRoot).IsTrue();
        var entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
        await Assert.That(entries.Length).IsEqualTo(PartitionHostApplicationProtocol.OneFailure);
        await Assert.That(Path.GetFileName(entries[PartitionHostApplicationProtocol.FailureIndex]))
            .IsEqualTo(PartitionHostApplicationProtocol.OwnershipFile);
        await Assert.That(new FileInfo(entries[PartitionHostApplicationProtocol.FailureIndex]).Length)
            .IsEqualTo(PartitionHostApplicationProtocol.EmptyFileLength);
    }

    internal static async Task ExcludedAsync(string directory)
    {
        var path = Path.Combine(directory, PartitionHostApplicationProtocol.OwnershipFile);
        var observed = new List<Exception>();
        FileStream? competing = null;
        ServerFailureObserver.Observe(() => competing = new FileStream(path, FileMode.Open,
            FileAccess.ReadWrite, FileShare.None), observed);
        if (competing is not null)
        {
            ServerFailureObserver.Observe(competing.Dispose, observed);
            ServerFailureObserver.ThrowIfAny(observed);
            throw new InvalidOperationException(PartitionHostApplicationProtocol.LockWasNotExclusive);
        }
        if (observed.Count != PartitionHostApplicationProtocol.OneFailure ||
            observed[PartitionHostApplicationProtocol.FailureIndex] is not IOException)
        {
            ServerFailureObserver.ThrowIfAny(observed);
            throw new InvalidOperationException(PartitionHostApplicationProtocol.MissingFailure);
        }
        await Assert.That(observed[PartitionHostApplicationProtocol.FailureIndex] is IOException).IsTrue();
    }
}
