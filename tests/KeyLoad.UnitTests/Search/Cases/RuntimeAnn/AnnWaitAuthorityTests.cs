namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnWaitAuthorityTests
{
    private const long NextPosition = 1;
    private const long ZeroPosition = 0;
    private const int WrongIncarnationCase = 0;
    private const int WrongAtomicCase = 1;
    private const int FutureCase = 2;
    private const int ZeroCase = 3;
    private const string WrongAtomic = "different-atomic-partition";

    [Test]
    [Arguments(WrongIncarnationCase)]
    [Arguments(WrongAtomicCase)]
    [Arguments(FutureCase)]
    [Arguments(ZeroCase)]
    public Task RealReplicatedMinimumRejectsWrongFutureOrZeroThenActualPinnedPrefixIsHealthy(int defect)
        => AnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var request = AnnWaitWholeFlow.Request(database, pin, receipt);
            var invalid = defect switch
            {
                WrongIncarnationCase => receipt.Token with { Incarnation = Guid.NewGuid() },
                WrongAtomicCase => receipt.Token with { AtomicPartitionId = WrongAtomic },
                FutureCase => receipt.Token with { Position = database.Database.LastApplied + NextPosition },
                ZeroCase => receipt.Token with { Position = ZeroPosition },
                _ => throw new ArgumentOutOfRangeException(nameof(defect))
            };
            var image = AnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            WaitForAnnIndexResult? partial = null;
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                await AnnPublicWholeFlow.Engine(database, runtime).WaitForAnnIndexAsync(AnnWaitWholeFlow.Root,
                    request with
                    {
                        MinimumToken = invalid
                    }, TestContext.Current!.Execution.CancellationToken))
                ?? throw new InvalidOperationException();
            await Assert.That(error.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await AnnWaitWholeFlow.HealthyAsync(database, runtime, request, TestContext.Current!.Execution.CancellationToken);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task MissingOrNegativeAppliedRowRefusesWithoutPartialThenExactRepairReturnsFullHealthyWaitAndSearch(bool missing)
        => AnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var request = AnnWaitWholeFlow.Request(database, pin, receipt);
            var originalImage = AnnMaintenanceTestData.Snapshot(database);
            var original = database.Store.Read(view => view.ReadOwnedValue(KeyLoad.Core.KeySpace.AppliedBytes))
                ?? throw new InvalidOperationException();
            var failures = new List<Exception>();
            try
            {
                await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    database.Store.Commit((transaction, _) =>
                    {
                        if (missing)
                        {
                            transaction.Delete(KeyLoad.Core.KeySpace.AppliedBytes);
                        }
                        else
                        {
                            transaction.Put(KeyLoad.Core.KeySpace.AppliedBytes, OrleansSerialization.Serialize(long.MinValue));
                        }
                        return true;
                    });
                    var faultedImage = AnnMaintenanceTestData.Snapshot(database);
                    var faultedCut = database.Store.Position;
                    WaitForAnnIndexResult? partial = null;
                    var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                        await AnnPublicWholeFlow.Engine(database, runtime).WaitForAnnIndexAsync(AnnWaitWholeFlow.Root,
                            request, TestContext.Current!.Execution.CancellationToken))
                        ?? throw new InvalidOperationException();
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
                    await Assert.That(partial).IsNull();
                    await AnnPublicWholeFlow.UnchangedAsync(database, faultedImage, faultedCut);
                }, failures);
            }
            finally
            {
                KeyLoad.Server.ServerFailureObserver.Observe(() => database.Store.Commit((transaction, _) =>
                {
                    transaction.Put(KeyLoad.Core.KeySpace.AppliedBytes, original);
                    return true;
                }), failures);
            }
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            var repairedCut = database.Store.Position;
            await AnnPublicWholeFlow.UnchangedAsync(database, originalImage, repairedCut);
            await AnnWaitWholeFlow.HealthyAsync(database, runtime, request, TestContext.Current!.Execution.CancellationToken);
            var page = await AnnPublicWholeFlow.Engine(database, runtime).ApproximateSearchAsync(AnnWaitWholeFlow.Root,
                AnnPublicWholeFlow.Request(database, pin), TestContext.Current!.Execution.CancellationToken);
            await AnnPublicWholeFlow.PageAsync(page, database, repairedCut, AnnPageMode.Exact);
            await AnnPublicWholeFlow.UnchangedAsync(database, originalImage, repairedCut);
        });
}
