using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkMultiblockPagedRetentionFlow
{
    private const int ColdOwners = 2;
    private const long OwnershipEpoch = 1;
    private const long CommitIncrement = 1;
    private const string ExpireKind = "expireSamples";

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SampleChunkPartialRetentionFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(SampleChunkPartialRetentionFixture fixture)
    {
        ChunkMultiblockPagedRetentionOperations.Seed(fixture);
        await ChunkMultiblockPagedRetentionRows.RequireAsync(fixture, expired: false, healthy: false);
        await ChunkMultiblockPagedRetentionRepresentation.RecordsAsync(fixture, healthy: false);
        var native = ChunkMultiblockPagedRetentionRepresentation.Capture(fixture, ChunkMultiblockBoundaryData.InitialGeneration);
        fixture.Clock.AdvanceTo(SampleChunkCanonicalFixture.Late.Timestamp);
        await PageAsync(fixture, native, ChunkMultiblockPagedRetentionOperations.DeletePage, more: true);
        await PageAsync(fixture, native, ChunkMultiblockPagedRetentionOperations.ExpiredCount, more: false);
        await ChunkMultiblockPagedRetentionRefusal.RunAsync(fixture);
        await ColdAsync(fixture, healthy: false);
        ChunkMultiblockPagedRetentionOperations.Healthy(fixture);
        await ChunkMultiblockPagedRetentionRows.RequireAsync(fixture, expired: true, healthy: true);
        await ChunkMultiblockPagedRetentionRepresentation.RecordsAsync(fixture, healthy: true);
        await SampleChunkPartialRetentionAssertions.Replay(fixture);
        await ColdAsync(fixture, healthy: true);
    }

    private static async Task PageAsync(SampleChunkPartialRetentionFixture fixture, byte[][] native, long purged, bool more)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var before = SampleRawIntegrityAssertions.Capture(fixture.Store);
            var authority = fixture.Store.Identity;
            var issued = ChunkMultiblockPagedRetentionOperations.Expire(fixture);
            var expected = new CommitReceipt(issued.Operation.Id,
                new(authority.Incarnation, fixture.Partition.AtomicPartitionId, checked(before.Position + CommitIncrement), OwnershipEpoch),
                [new(ExpireKind, SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series, purged)], authority.Durability);
            await Assert.That(JsonDefaults.Serialize(issued.Receipt).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
            await ChunkMultiblockPagedRetentionRows.PageAsync(fixture, purged, more);
            await ChunkMultiblockPagedRetentionRows.RequireAsync(fixture, expired: true, healthy: false);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkMultiblockPagedRetentionRepresentation.UnchangedAsync(fixture, native), failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkPartialRetentionAssertions.Replay(fixture), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(SampleChunkPartialRetentionFixture fixture, bool healthy)
    {
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            var cut = SampleRawIntegrityAssertions.Capture(fixture.Store);
            fixture.Reopen();
            await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
            await ChunkMultiblockPagedRetentionRows.RequireAsync(fixture, expired: true, healthy);
            await ChunkMultiblockPagedRetentionRepresentation.RecordsAsync(fixture, healthy);
            await SampleChunkPartialRetentionAssertions.Replay(fixture);
            await SampleRawIntegrityAssertions.UnchangedAsync(fixture.Store, cut);
        }
    }
}
