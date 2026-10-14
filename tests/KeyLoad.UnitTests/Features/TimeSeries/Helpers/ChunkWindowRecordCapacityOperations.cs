using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class ChunkWindowRecordCapacityOperations
{
    private const string OpenKind = "openSampleChunkWindow";
    private const string AppendKind = "appendSamples";
    private const string SealKind = "sealSampleChunkWindow";
    private const string MergeKind = "mergeSampleChunkWindow";
    private const string DropKind = "dropSampleChunkWindow";
    private const string ExpireKind = "expireSamples";
    private const string Exhausted = "The native series chunk window exceeds its admitted bound.";
    private const int PageLimit = 128;
    private const int WindowHours = 1;
    private const long CorrectedRevision = 259;
    private const long DroppedRevision = 261;
    private const long FreshSource = 258;
    private const long FreshAppendRevision = 2;
    private const long FreshSealedRevision = 3;
    private const string FreshId = "record-capacity-healthy";
    private const double FreshValue = 8;

    internal static SampleData Fresh => new(FreshId, SampleChunkCanonicalFixture.Until, FreshValue);

    internal static async Task SeedAsync(SampleChunkCanonicalFixture fixture,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        await CommitAsync(fixture, new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until), OpenKind, SampleChunkCanonicalFixture.FirstRevision, originals);
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags), AppendKind, SampleChunkCanonicalFixture.InitialSequence, originals);
        await CommitAsync(fixture, new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision),
            SealKind, SampleChunkCanonicalFixture.SealedRevision, originals);
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags), AppendKind, SampleChunkCanonicalFixture.CorrectedSequence, originals);
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            ChunkMultiblockBoundaryData.Additional, SampleChunkCanonicalFixture.Tags), AppendKind, ChunkMultiblockBoundaryData.InitialCount, originals);
        await CommitAsync(fixture, new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, CorrectedRevision), MergeKind, ChunkMultiblockBoundaryData.InitialRevision, originals);
    }

    internal static async Task RefuseAsync(SampleChunkCanonicalFixture fixture,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        var request = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                [ChunkSelectedIntegrityFixture.Fresh], SampleChunkCanonicalFixture.Tags)]);
        var operation = new ReplicatedOperation(request.CommandId, OperationKind.Batch,
            SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var raw = fixture.Raw();
        var failures = new List<Exception>();
        OperationResult? refused = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            refused = fixture.Owner.Database.ApplyEmbedded(operation, cancellationToken: default);
            originals.Add((operation, refused));
            var expected = new OperationResult(null, ErrorCode.ResourceExhausted, Exhausted);
            await Assert.That(SampleRollupWholeFlow.Outcome(refused)).IsEqualTo(SampleRollupWholeFlow.Outcome(expected));
            await Assert.That(refused.Error).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(refused.Json).IsNull();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(fixture.Raw()).IsEqualTo(raw), failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkWindowRecordCapacityAssertions.InitialAsync(
            fixture, expired: false), failures);
        if (refused is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                fixture.Owner.Database, fixture.Owner.Store, operation, refused), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static Task ExpireAsync(SampleChunkCanonicalFixture fixture, long purged,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
        => CommitAsync(fixture, new ExpireSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Until, PageLimit), ExpireKind, purged, originals);

    internal static async Task HealthyAsync(SampleChunkCanonicalFixture fixture, Guid freshWindow,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        await CommitAsync(fixture, new DropSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, ChunkMultiblockBoundaryData.InitialRevision),
            DropKind, DroppedRevision, originals);
        await CommitAsync(fixture, new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, freshWindow, SampleChunkCanonicalFixture.Until,
            SampleChunkCanonicalFixture.Until.AddHours(WindowHours)), OpenKind, SampleChunkCanonicalFixture.FirstRevision, originals);
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [Fresh], SampleChunkCanonicalFixture.Tags), AppendKind, FreshSource, originals);
        await CommitAsync(fixture, new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, freshWindow, FreshAppendRevision), SealKind, FreshSealedRevision, originals);
    }

    private static Task CommitAsync(SampleChunkCanonicalFixture fixture, Mutation mutation, string kind, long revision,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
        => SampleChunkCorrectionBoundReceipts.CommitAsync(fixture, mutation, kind, revision, originals);
}
