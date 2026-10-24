using KeyLoad.Server;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphLabelUseFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var db = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = await GraphLabelUseFixture.SeedAsync(db);
                await GraphLabelUseFixture.PersistAsync(db, false, 1L);
                await GraphLabelUseOperations.ReadAsync(db.Database, db.Store, db, false, token);
                await GraphLabelUseFault.RunAsync(db, token);
                await GraphLabelUseFixture.PersistAsync(db, true, 2L);
                await GraphLabelUseOperations.ReadAsync(db.Database, db.Store, db, true, token);
                await CancelledAsync(db);
                await GraphLabelUseFixture.PersistAsync(db, false, 3L);
                await GraphLabelUseOperations.ReadAsync(db.Database, db.Store, db, false, token);
                await GraphLabelUseFixture.PersistAsync(db, true, 4L);
                await GraphLabelUseOperations.ReadAsync(db.Database, db.Store, db, true, token);
                await GraphLabelUseOperations.ReplayAsync(db.Database, db.Store, seed.Command, seed.Receipt, seed.Outcome);
                await GraphLabelUseCold.RunAsync(db, seed.Command, seed.Receipt, seed.Outcome, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static Task CancelledAsync(TestDatabase db)
        => GraphLabelUseOperations.ObserveAsync(db.Store, async () =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var cancelled = new CancellationTokenSource();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await cancelled.CancelAsync();
                    var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
                        GraphLabelUseFixture.Traverse(db.Database, db.Partition, ["route"], cancelled.Token));
                    await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        });
}
