using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class StoreOwnerEntryHealthy
{
    private const int ColdCycles = 2;
    private const long CommittedPosition = 1;
    private const string FreshSuffix = ".fresh";
    private const string EmptyOwnerSuffix = ".empty-owner";

    internal static async Task ReopenAsync(ZoneTreeExistingStoreFixture files, StoreOwnerEntryLifetime lifetime)
    {
        var failures = new List<Exception>();
        for (var cold = 0; cold < ColdCycles; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var result = await files.InspectAsync();
                await ExistingStoreInspectionAssertions.SucceededAsync(result, files.Identity, files.Value, CommittedPosition);
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => ReadAsync(files.Options, files.Identity, files, lifetime), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task FreshAsync(ZoneTreeExistingStoreFixture files, StoreOwnerEntryLifetime lifetime)
    {
        await CreateAsync(files.DirectoryPath + FreshSuffix, createEmptyOwner: false, files, lifetime);
        await CreateAsync(files.DirectoryPath + EmptyOwnerSuffix, createEmptyOwner: true, files, lifetime);
    }

    private static async Task CreateAsync(string directory, bool createEmptyOwner, ZoneTreeExistingStoreFixture files, StoreOwnerEntryLifetime lifetime)
    {
        var failures = new List<Exception>();
        Directory.CreateDirectory(directory);
        if (createEmptyOwner)
        {
            CreateEmptyOwner(directory, lifetime, failures);
        }
        if (!lifetime.CanContinue)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        StoreIdentity? identity = null;
        var store = lifetime.Acquire(() => new(new(directory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        if (store is not null)
        {
            ZoneTreeStore? attempted = null;
            var attemptedOpen = false;
            var refused = false;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                identity = store.Identity;
                store.Commit((transaction, _) =>
                {
                    transaction.Put(files.Key, files.Value);
                    return true;
                });
                var failure = Assert.ThrowsExactly<IOException>(() =>
                {
                    attemptedOpen = true;
                    attempted = new ZoneTreeStore(new(directory),
                        UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
                    lifetime.Own(attempted);
                });
                await Assert.That(failure).IsNotNull();
                refused = true;
                await Assert.That(store.Position).IsEqualTo(CommittedPosition);
            }, failures);
            if (attempted is not null)
            {
                lifetime.Close(attempted, failures);
            }
            else if (attemptedOpen && !refused)
            {
                lifetime.RetainUnknown();
            }
            var closed = lifetime.Close(store, failures);
            if (closed && lifetime.CanContinue && identity is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => ReadAsync(new(directory), identity, files, lifetime), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void CreateEmptyOwner(string directory, StoreOwnerEntryLifetime lifetime, List<Exception> failures)
    {
        FileStream? empty = null;
        ServerFailureObserver.Observe(() =>
        {
            empty = File.Create(Path.Combine(directory, ZoneTreeExistingStoreFixture.OwnerFile));
            lifetime.Own(empty);
        }, failures);
        if (empty is not null)
        {
            lifetime.Close(empty, failures);
        }
        else
        {
            lifetime.RetainUnknown();
        }
    }

    private static async Task ReadAsync(ZoneTreeStoreOptions options, StoreIdentity expected,
        ZoneTreeExistingStoreFixture files, StoreOwnerEntryLifetime lifetime)
    {
        var failures = new List<Exception>();
        var reopened = lifetime.Acquire(() => new(options,
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        if (reopened is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var scalarIdentity = reopened.Identity with
                {
                    SigningKey = expected.SigningKey
                };
                await Assert.That(scalarIdentity).IsEqualTo(expected);
                await Assert.That(reopened.Identity.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
                await Assert.That(reopened.Position).IsEqualTo(CommittedPosition);
                await Assert.That(reopened.Read(view => view.ReadOwnedValue(files.Key)))
                    .IsEquivalentTo(files.Value, CollectionOrdering.Matching);
            }, failures);
            lifetime.Close(reopened, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
