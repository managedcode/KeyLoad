using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphSearchIntegrityFlow : IDisposable
{
    private const long RepairCommit = 1L;
    private const int NoFailures = 0;
    private readonly TestDatabase database = new();
    private bool safeToDelete = true;

    internal static async Task RunAsync(GraphSearchIntegrityOperator selected,
        GraphTraversalIntegrityFault fault, bool filtered)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var flow = new GraphSearchIntegrityFlow();
            await ServerFailureObserver.ObserveAsync(() => flow.RunOwnedAsync(selected, fault, filtered, failures), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        if (safeToDelete)
        {
            database.Dispose();
        }
    }

    private async Task RunOwnedAsync(GraphSearchIntegrityOperator selected,
        GraphTraversalIntegrityFault fault, bool filtered, List<Exception> failures)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = GraphTraversalIntegrityFixture.Seed(database);
        var identity = database.Store.Identity;
        var receipt = GraphSearchIntegrityRecipe.SeedReceipt(state, identity.Incarnation, database.Database.LastApplied);
        await GraphSearchIntegrityAssertions.ReceiptAsync(receipt, state.SeedReceipt);
        var outcome = GraphSearchIntegrityAssertions.Outcome(database.Store, state.SeedCommand);
        await GraphSearchIntegrityAssertions.ReadAsync(database.Database, database.Store, state, null, selected, filtered, token);
        var healthyImage = CanonicalStoreImage.Bytes(database.Store);
        var owned = GraphTraversalIntegrityFaultRows.Install(database.Store, state, fault);
        await GraphSearchIntegrityAssertions.RefuseAsync(database.Database, database.Store, state, selected, filtered, fault, token);
        await GraphSearchIntegrityAssertions.RefuseAsync(database.Database, database.Store, state, selected, filtered, fault, token);
        var dirtyImage = CanonicalStoreImage.Bytes(database.Store);
        var dirtyCut = database.Store.Position;
        safeToDelete = false;
        database.JoinOwnedResources();
        GraphTraversalIntegrityContinuation? continuation = null;
        byte[]? continuedOutcome = null;
        string[]? finalImage = null;
        var finalCut = dirtyCut;
        await ColdAsync(database, async store =>
        {
            var owner = database.CreateConfiguredDatabase(store);
            await GraphSearchIntegrityAssertions.IdentityAsync(identity, store.Identity);
            await GraphTraversalIntegrityAssertions.Image(store, dirtyImage, dirtyCut);
            await GraphSearchIntegrityAssertions.RefuseAsync(owner, store, state, selected, filtered, fault, token);
            GraphTraversalIntegrityFaultRows.Repair(store, owned);
            await GraphTraversalIntegrityAssertions.Image(store, healthyImage, dirtyCut + RepairCommit);
            await GraphSearchIntegrityAssertions.ReadAsync(owner, store, state, null, selected, filtered, token);
            await GraphSearchIntegrityAssertions.ReplayAsync(owner, store, state.SeedCommand, receipt, outcome);
            var position = checked(owner.LastApplied + RepairCommit);
            continuation = GraphTraversalIntegrityFixture.Continue(owner, state);
            await GraphSearchIntegrityAssertions.ReceiptAsync(
                GraphSearchIntegrityRecipe.ContinuedReceipt(continuation, identity.Incarnation, position), continuation.Receipt);
            continuedOutcome = GraphSearchIntegrityAssertions.Outcome(store, continuation.Command);
            await GraphSearchIntegrityAssertions.ReadAsync(owner, store, state, continuation, selected, filtered, token);
            await GraphSearchIntegrityAssertions.ReplayAsync(owner, store, continuation.Command, continuation.Receipt, continuedOutcome);
            finalImage = CanonicalStoreImage.Bytes(store);
            finalCut = store.Position;
        }, () => safeToDelete = true, failures);
        if (failures.Count != NoFailures)
        {
            return;
        }
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(continuedOutcome);
        ArgumentNullException.ThrowIfNull(finalImage);
        safeToDelete = false;
        await ColdAsync(database, async store =>
        {
            var owner = database.CreateConfiguredDatabase(store);
            await GraphSearchIntegrityAssertions.IdentityAsync(identity, store.Identity);
            await GraphTraversalIntegrityAssertions.Image(store, finalImage, finalCut);
            await GraphSearchIntegrityAssertions.ReadAsync(owner, store, state, continuation, selected, filtered, token);
            await GraphSearchIntegrityAssertions.ReplayAsync(owner, store, state.SeedCommand, receipt, outcome);
            await GraphSearchIntegrityAssertions.ReplayAsync(owner, store, continuation.Command, continuation.Receipt, continuedOutcome);
        }, () => safeToDelete = true, failures);
    }

    private static async Task ColdAsync(TestDatabase database, Func<ZoneTreeStore, Task> body,
        Action joined, List<Exception> failures)
        => await ServerFailureObserver.ObserveAsync(async () =>
        {
            using (var store = new ZoneTreeStore(new(database.Directory)
            {
                Incarnation = database.Store.Identity.Incarnation
            }, database.StorageExecution, UnitExecutionOptions.PointCacheExecution(), database.Database.EvaluationClock))
            {
                await ServerFailureObserver.ObserveAsync(() => body(store), failures);
            }
            joined();
        }, failures);
}
