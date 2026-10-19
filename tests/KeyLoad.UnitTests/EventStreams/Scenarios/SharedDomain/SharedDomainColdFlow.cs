using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainColdFlow
{
    private const int ColdOwners = 2;
    private const int FirstCold = 0;
    private const string MissingRoot = "The original shared-domain store root must remain canonical, existing and nonempty.";
    private const string MissingCut = "The actual cold shared-domain owner returned no verified cut.";

    internal static async Task RunAsync(TestDatabase fixture, SharedDomainState state, CancellationToken token)
    {
        var cut = Capture(fixture.Store);
        fixture.JoinOwnedResources();
        for (var cycle = FirstCold; cycle < ColdOwners; cycle++)
        {
            cut = await OpenOwnedAsync(fixture, state, cut, cycle == FirstCold, token);
        }
    }

    private static async Task<SharedDomainCut> OpenOwnedAsync(TestDatabase fixture, SharedDomainState state,
        SharedDomainCut cut, bool continueHealthy, CancellationToken token)
    {
        RequireOriginalRoot(fixture.Directory);
        SharedDomainCut? result = null;
        List<Exception> failures = [];
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Directory)
            {
                Incarnation = cut.Identity.Incarnation,
                SigningKey = cut.Identity.SigningKey
            },
                fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                result = await ExecuteAsync(fixture.CreateConfiguredDatabase(store), store, state, cut, continueHealthy, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingCut);
    }

    private static async Task<SharedDomainCut> ExecuteAsync(DatabaseEngine database, ZoneTreeStore store, SharedDomainState state,
        SharedDomainCut cut, bool continueHealthy, CancellationToken token)
    {
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(store, cut.Identity, cut.Position);
        await SharedDomainImage.SameAsync(store, cut.Rows, cut.Position);
        await SharedDomainModelAssertions.AllAsync(database, state, token);
        await SharedDomainHistoryOperations.AllAsync(database, store, state, token);
        if (continueHealthy)
        {
            await SharedDomainAuthorityFlow.RunAsync(database, state, token);
            await SharedDomainModelAssertions.AllAsync(database, state, token);
            cut = Capture(store);
        }
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(store, cut.Identity, cut.Position);
        await SharedDomainImage.SameAsync(store, cut.Rows, cut.Position);
        return cut;
    }

    private static SharedDomainCut Capture(ZoneTreeStore store)
        => new(ReadCutStoreStateAssertions.SnapshotIdentity(store), store.Position, SharedDomainImage.Capture(store));

    private static void RequireOriginalRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root) || !string.Equals(Path.GetFullPath(root), root, StringComparison.Ordinal)
            || !System.IO.Directory.Exists(root) || !System.IO.Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException(MissingRoot);
        }
    }
}
