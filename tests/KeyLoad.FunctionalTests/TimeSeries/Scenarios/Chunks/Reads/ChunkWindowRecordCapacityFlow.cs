using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class ChunkWindowRecordCapacityFlow
{
    private const int Ceiling = 257;
    private const long PurgedFirstPage = 128;
    private const long PurgedSecondPage = 256;
    private const long PurgedLastPage = 257;

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var clock = new SampleChunkRetentionClock(SampleChunkCanonicalFixture.Start);
            using var fixture = new SampleChunkCanonicalFixture(new()
            {
                MaximumChunkWindowRecords = Ceiling
            }, clock);
            var freshWindow = Guid.NewGuid();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture, clock, freshWindow), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(SampleChunkCanonicalFixture fixture,
        SampleChunkRetentionClock clock, Guid freshWindow)
    {
        var originals = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        await ChunkWindowRecordCapacityOperations.SeedAsync(fixture, originals);
        await ChunkWindowRecordCapacityAssertions.InitialAsync(fixture, expired: false);
        var immutable = ChunkWindowRecordCapacityAssertions.Capture(fixture.Owner.Store, fixture);
        await ChunkWindowRecordCapacityOperations.RefuseAsync(fixture, originals);
        clock.AdvanceTo(SampleChunkCanonicalFixture.Until);
        await PageAsync(fixture, originals, immutable, PurgedFirstPage, more: true);
        await PageAsync(fixture, originals, immutable, PurgedSecondPage, more: true);
        await PageAsync(fixture, originals, immutable, PurgedLastPage, more: false);
        await ChunkWindowRecordCapacityOperations.HealthyAsync(fixture, freshWindow, originals);
        await ChunkWindowRecordCapacityCold.RunAsync(fixture, clock, freshWindow, originals);
    }

    private static async Task PageAsync(SampleChunkCanonicalFixture fixture,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals, string[] immutable,
        long purged, bool more)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityOperations.ExpireAsync(
            fixture, purged, originals), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.InitialAsync(
            fixture, expired: true), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.RetentionAsync(
            fixture, purged, more), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.PhysicalAsync(
            fixture.Owner.Store, fixture, purged, fresh: false), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = ChunkWindowRecordCapacityAssertions.Capture(fixture.Owner.Store, fixture);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(immutable))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityCold.ReplayAsync(
            fixture.Owner.Database, fixture.Owner.Store, originals), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
