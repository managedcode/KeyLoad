using System.Text.Json;
using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleChunkAtomicRollbackFlow
{
    private const string OpenKind = "openSampleChunkWindow";
    private const string AppendKind = "appendSamples";
    private const string SealKind = "sealSampleChunkWindow";
    private const string MergeKind = "mergeSampleChunkWindow";
    private const string RevisionConflict = "The execution series chunk window revision changed.";

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SampleChunkCanonicalFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(SampleChunkCanonicalFixture fixture)
    {
        var originals = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        await CommitAsync(fixture, new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until), OpenKind, SampleChunkCanonicalFixture.FirstRevision, originals);
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags),
            AppendKind, SampleChunkCanonicalFixture.InitialSequence, originals);
        await RefuseAsync(fixture, originals);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.AppendedRevision,
            SampleChunkCanonicalFixture.NoGeneration, SampleChunkCanonicalFixture.InitialSequence);
        await CommitAsync(fixture, new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision),
            SealKind, SampleChunkCanonicalFixture.SealedRevision, originals);
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags),
            AppendKind, SampleChunkCanonicalFixture.CorrectedSequence, originals);
        await CommitAsync(fixture, new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.CorrectedRevision),
            MergeKind, SampleChunkCanonicalFixture.MergedRevision, originals);
        await SampleChunkCanonicalAssertions.Literal(fixture, SampleChunkCanonicalFixture.MergedRevision,
            SampleChunkCanonicalFixture.MergedGeneration, SampleChunkCanonicalFixture.CorrectedSequence, true);
        await SampleChunkAtomicRollbackCold.RunAsync(fixture, originals);
    }

    private static Task CommitAsync(SampleChunkCanonicalFixture fixture, Mutation mutation, string kind, long revision,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
        => SampleChunkCorrectionBoundReceipts.CommitAsync(fixture, mutation, kind, revision, originals);

    private static async Task RefuseAsync(SampleChunkCanonicalFixture fixture,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var request = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                [SampleChunkCanonicalFixture.Late], SampleChunkCanonicalFixture.Tags),
             new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                fixture.WindowId, SampleChunkCanonicalFixture.FirstRevision)]);
        var operation = fixture.Owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var raw = fixture.Raw();
        var originalRows = Convert.ToHexString(JsonDefaults.Serialize(fixture.Read().Records));
        var failures = new List<Exception>();
        Result<object>? rejected = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = fixture.Owner.Database.ApplyEmbedded(operation, cancellationToken: default);
            rejected = actual;
            originals.Add((operation, actual));
            var expected = Result<object>.Fail(Errors.Problem(ErrorCode.RevisionConflict, RevisionConflict));
            await Assert.That(SampleRollupWholeFlow.Outcome(actual)).IsEqualTo(SampleRollupWholeFlow.Outcome(expected));
            await Assert.That(Errors.Code(actual.Problem).Value).IsEqualTo(ErrorCode.RevisionConflict);

        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(fixture.Raw()).IsEqualTo(raw), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(fixture.Read().Records))).IsEqualTo(originalRows), failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkAtomicRollbackCold.RowsAsync(
            fixture.Owner.Database, fixture.Owner.Store, fixture, healthy: false), failures);
        if (rejected is { } retained)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                fixture.Owner.Database, fixture.Owner.Store, operation, retained), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
