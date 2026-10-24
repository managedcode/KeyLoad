using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Search;

internal static class AnnPublicReservationFlow
{
    private const long ResidentBytes = 1_048_576;
    private const int Readers = 2;
    private const long OneByte = 1;
    private const long NoBytes = 0;
    private const int Split = 2;
    private const float QueryXAxis = 1f;
    private const float QueryUnusedAxis = 0f;
    private static readonly string[] ExpectedDocumentIds = ["seed-00002", "seed-00001", "seed-00000"];

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = AnnSeedTestSupport.Create(AnnPublicWholeFlow.Count);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var pin = AnnMaintenanceTestData.Pin(database);
                var options = new AnnExecutionOptions { MaximumResidentBytes = ResidentBytes };
                var policy = Options.Create(new PackedAnnOptions());
                var read = Read(database);
                var seeds = Options.Create(new AnnSeedOptions());
                var source = AnnSeedCollector.CapturePinned(database.Database, AnnWaitWholeFlow.Root, pin, seeds, read);
                var page = database.Database.ReadProjectionBatch(AnnWaitWholeFlow.Root,
                    new(pin.Consumer, AnnMaintenanceTestData.SnapshotBound,
                        AnnMaintenanceTestData.PageBytes, source.Cut.OutboxTail));
                await Assert.That(page.HasMore).IsFalse();
                await Assert.That(page.ThroughSequence).IsEqualTo(source.Cut.OutboxTail);
                var intent = AnnMaintenanceTestData.Intent(page);
                await using var owner = AnnOwnership.Create(database.Directory, database.Store.Identity,
                    Options.Create(options), policy, Options.Create(new PackedAnnStorageOptions()), Readers);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    _ = await owner.RunAsync(_ => owner.BuildAndPublish(pin, source, intent,
                        new AnnWorkBudget(read, seeds.Value.MaxWorkUnits)));
                    _ = await AnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
                    var restore = pin with
                    {
                        Mode = AnnMaintenanceMode.Restore
                    };
                    var current = AnnSeedCollector.CapturePinned(database.Database, AnnWaitWholeFlow.Root,
                        restore, seeds, Read(database));
                    var index = owner.Acquire(restore, current);
                    try
                    {
                        await ServerFailureObserver.ObserveAsync(() => RequireAsync(database, owner, index,
                            options), failures);
                    }
                    finally { ServerFailureObserver.Observe(index.Dispose, failures); }
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ReadExecutionBudget Read(TestDatabase database)
        => new(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Database.EvaluationClock,
            TestContext.Current!.Execution.CancellationToken);

    private static async Task RequireAsync(TestDatabase database, AnnGenerationOwner owner,
        AnnIndexLease index, AnnExecutionOptions options)
    {
        var receipt = AnnRootLease.ReceiptReservation(options);
        var indexed = index.Index.RetainedBytesUpperBound;
        var free = checked(options.MaximumResidentBytes - receipt - indexed);
        await Assert.That(free > NoBytes).IsTrue();
        var image = AnnMaintenanceTestData.Snapshot(database);
        var cut = database.Store.Position;
        using (var full = owner.ReserveRead(long.MaxValue))
        {
            await Assert.That(full.Bytes).IsEqualTo(free);
            await Assert.That(checked(receipt + indexed + full.Bytes)).IsEqualTo(options.MaximumResidentBytes);
            await RefuseAsync(owner);
        }
        using (var first = owner.ReserveRead(free / Split))
        using (var second = owner.ReserveRead(long.MaxValue))
        {
            await Assert.That(checked(first.Bytes + second.Bytes)).IsEqualTo(free);
            await RefuseAsync(owner);
        }
        using (var following = owner.ReserveRead(long.MaxValue))
        {
            await Assert.That(following.Bytes).IsEqualTo(free);
            var actual = index.Index.Search(new float[] { QueryXAxis, QueryUnusedAxis, QueryUnusedAxis }, AnnPublicWholeFlow.Count, null,
                new AnnWorkBudget(Read(database), new AnnSeedOptions().MaxWorkUnits));
            await Assert.That(actual.Candidates.Select(row => row.DocumentId).ToArray()).IsEquivalentTo(
                ExpectedDocumentIds, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(actual.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        }
        await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
    }

    private static async Task RefuseAsync(AnnGenerationOwner owner)
    {
        AnnReadReservation? unexpected = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(() => unexpected = owner.ReserveRead(OneByte));
                await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            }, failures);
        }
        finally
        {
            if (unexpected is not null)
            {
                ServerFailureObserver.Observe(unexpected.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
