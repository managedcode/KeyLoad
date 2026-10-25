using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class WriteThroughJournalFlow
{
    private const int ColdCycles = 2;
    private const string ControlFile = "original-bcl-write-through.control";
    private const byte ControlByte = 0x5A;
    private const string ControlChanged = "The actual BCL write-through control bytes changed.";

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        ZoneTreeExistingStoreFixture? files = null;
        ServerFailureObserver.Observe(() => files = new(), failures);
        if (files is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => ContinueAsync(files, lifetime), failures);
            if (lifetime.CanAdvance)
            {
                ServerFailureObserver.Observe(files.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ContinueAsync(ZoneTreeExistingStoreFixture files, StorageFailureOwnerLifetime lifetime)
    {
        var failures = new List<Exception>();
        var originalFlags = OriginalBclFlags(files, lifetime, failures);
        if (lifetime.CanAdvance && failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(
                () => WriteThroughJournalOwner.RunAsync(files, lifetime, originalFlags, publish: true), failures);
        }
        for (var cold = 0; cold < ColdCycles && lifetime.CanAdvance; cold++)
        {
            await ServerFailureObserver.ObserveAsync(
                () => WriteThroughJournalOwner.RunAsync(files, lifetime, originalFlags, publish: false), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static int OriginalBclFlags(ZoneTreeExistingStoreFixture files,
        StorageFailureOwnerLifetime lifetime, List<Exception> failures)
    {
        var flags = 0;
        var path = Path.Combine(files.DirectoryPath, ControlFile);
        var owner = lifetime.Acquire(() => new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read, UnitExecutionOptions.StorageExecution().Value.FileBufferBytes, FileOptions.WriteThrough), failures);
        if (owner is not null)
        {
            ServerFailureObserver.Observe(() =>
            {
                owner.WriteByte(ControlByte);
                owner.Flush(true);
                flags = OpenStatusFlags.Read(owner);
            }, failures);
            lifetime.Close(owner, failures);
            if (lifetime.CanAdvance)
            {
                ServerFailureObserver.Observe(() =>
                {
                    var actual = File.ReadAllBytes(path);
                    if (!actual.AsSpan().SequenceEqual(new[] { ControlByte }))
                    {
                        throw new InvalidDataException(ControlChanged);
                    }
                }, failures);
            }
        }
        return flags;
    }
}
