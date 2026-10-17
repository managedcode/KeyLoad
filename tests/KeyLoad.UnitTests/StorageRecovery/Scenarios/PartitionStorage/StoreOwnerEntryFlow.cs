using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class StoreOwnerEntryFlow
{
    private const string SavedSuffix = ".original";
    private const string VictimSuffix = ".owner-canary";
    private const byte CanaryFirst = 0x17;
    private const byte CanarySecond = 0x92;

    internal static async Task RunAsync(Func<ZoneTreeExistingStoreFixture, StoreOwnerEntryLifetime, Task> body)
    {
        var failures = new List<Exception>();
        var lifetime = new StoreOwnerEntryLifetime();
        ZoneTreeExistingStoreFixture? files = null;
        ServerFailureObserver.Observe(() => files = new(), failures);
        if (files is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => body(files, lifetime), failures);
            if (lifetime.CanDeleteRoot)
            {
                ServerFailureObserver.Observe(files.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task SymlinkAsync(ZoneTreeExistingStoreFixture files, StoreOwnerEntryLifetime lifetime)
    {
        var original = await files.CaptureStoreEntriesAsync();
        var path = files.HideFile(ZoneTreeExistingStoreFixture.OwnerFile);
        var victim = files.DirectoryPath + VictimSuffix;
        byte[] canary = [CanaryFirst, CanarySecond];
        await File.WriteAllBytesAsync(victim, canary);
        File.CreateSymbolicLink(path, victim);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RefuseAsync(files, lifetime), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(await File.ReadAllBytesAsync(victim)).IsEquivalentTo(canary, CollectionOrdering.Matching), failures);
        if (lifetime.CanContinue)
        {
            ServerFailureObserver.Observe(() => File.Delete(path), failures);
        }
        await RestoreAsync(files, path, original, lifetime, failures);
    }

    internal static async Task DirectoryAsync(ZoneTreeExistingStoreFixture files, StoreOwnerEntryLifetime lifetime)
    {
        var original = await files.CaptureStoreEntriesAsync();
        var path = files.HideFile(ZoneTreeExistingStoreFixture.OwnerFile);
        Directory.CreateDirectory(path);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RefuseAsync(files, lifetime), failures);
        if (lifetime.CanContinue)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(path), failures);
        }
        await RestoreAsync(files, path, original, lifetime, failures);
    }

    internal static async Task MissingAsync(ZoneTreeExistingStoreFixture files, StoreOwnerEntryLifetime lifetime)
    {
        var original = await files.CaptureStoreEntriesAsync();
        var path = files.HideFile(ZoneTreeExistingStoreFixture.OwnerFile);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var result = await files.InspectAsync();
            await ExistingStoreInspectionAssertions.FailedAsync(result, null,
                ExistingStoreInspectionExpectedFailures.FileNotFound);
            await Assert.That(File.Exists(path)).IsFalse();
        }, failures);
        await RestoreAsync(files, path, original, lifetime, failures);
    }

    private static async Task RefuseAsync(ZoneTreeExistingStoreFixture files, StoreOwnerEntryLifetime lifetime)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? attempted = null;
        var refused = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            {
                attempted = new ZoneTreeStore(files.Options,
                    UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
                lifetime.Own(attempted);
            });
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
            refused = true;
        }, failures);
        if (attempted is not null)
        {
            lifetime.Close(attempted, failures);
        }
        else if (!refused)
        {
            lifetime.RetainUnknown();
        }
        if (lifetime.CanContinue)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var result = await files.InspectAsync();
                await ExistingStoreInspectionAssertions.FailedAsync(result,
                    ExistingStoreInspectionExpectedFailures.FormatUnsupported, ExistingStoreInspectionExpectedFailures.KeyLoad);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RestoreAsync(ZoneTreeExistingStoreFixture files, string path,
        IReadOnlyDictionary<string, byte[]?> original, StoreOwnerEntryLifetime lifetime, List<Exception> failures)
    {
        if (!lifetime.CanContinue)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        ServerFailureObserver.Observe(() => File.Move(path + SavedSuffix, path), failures);
        await ServerFailureObserver.ObserveAsync(() => files.AssertStoreEntriesUnchangedAsync(original), failures);
        await ServerFailureObserver.ObserveAsync(() => StoreOwnerEntryHealthy.ReopenAsync(files, lifetime), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
