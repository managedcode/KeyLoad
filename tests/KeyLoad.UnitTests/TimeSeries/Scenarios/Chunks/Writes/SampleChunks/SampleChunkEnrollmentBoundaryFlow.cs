using System.Text.Json;
using KeyLoad.Core.TimeSeries;
using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleChunkEnrollmentBoundaryFlow
{
    private const int OverlapMinutes = 30;
    private const string ConflictDetail = "The execution series chunk window overlaps retained data or an enrolled identity.";

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
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags),
            "appendSamples", SampleChunkCanonicalFixture.InitialSequence, originals);
        var occupied = new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            fixture.WindowId, SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until);
        await RefuseAsync(fixture, occupied, originals, () => SampleChunkEnrollmentBoundaryRows.RawAsync(
            fixture.Owner.Database, fixture.Owner.Store, fixture, populated: false));
        var enrolled = occupied with
        {
            WindowId = Guid.NewGuid(),
            From = occupied.Until,
            Until = SampleChunkEnrollmentBoundaryRows.Until
        };
        await CommitAsync(fixture, enrolled, "openSampleChunkWindow", SampleChunkCanonicalFixture.FirstRevision, originals);
        await SampleChunkEnrollmentBoundaryRows.CurrentAsync(fixture.Owner.Database, fixture.Owner.Store,
            fixture, enrolled.WindowId, populated: false, sealedWindow: false);
        var overlap = enrolled with
        {
            WindowId = Guid.NewGuid(),
            From = enrolled.From.AddMinutes(OverlapMinutes),
            Until = enrolled.Until.AddMinutes(OverlapMinutes)
        };
        await RefuseAsync(fixture, overlap, originals, () => SampleChunkEnrollmentBoundaryRows.CurrentAsync(
            fixture.Owner.Database, fixture.Owner.Store, fixture, enrolled.WindowId, populated: false, sealedWindow: false));
        await CommitAsync(fixture, new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            [SampleChunkEnrollmentBoundaryRows.Left, SampleChunkEnrollmentBoundaryRows.Right], SampleChunkCanonicalFixture.Tags),
            "appendSamples", SampleChunkEnrollmentBoundaryRows.RightSequence, originals);
        await SampleChunkEnrollmentBoundaryRows.CurrentAsync(fixture.Owner.Database, fixture.Owner.Store,
            fixture, enrolled.WindowId, populated: true, sealedWindow: false);
        await CommitAsync(fixture, new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, enrolled.WindowId, SampleChunkEnrollmentBoundaryRows.AppendedRevision), "sealSampleChunkWindow",
            SampleChunkEnrollmentBoundaryRows.SealedRevision, originals);
        var adjacentOccupied = enrolled with
        {
            WindowId = Guid.NewGuid(),
            From = enrolled.Until,
            Until = SampleChunkEnrollmentBoundaryRows.RawUntil
        };
        await RefuseAsync(fixture, adjacentOccupied, originals, () => SampleChunkEnrollmentBoundaryRows.CurrentAsync(
            fixture.Owner.Database, fixture.Owner.Store, fixture, enrolled.WindowId, populated: true, sealedWindow: true));
        await SampleChunkEnrollmentBoundaryCold.RunAsync(fixture, enrolled.WindowId, originals);
    }

    private static Task CommitAsync(SampleChunkCanonicalFixture fixture, Mutation mutation, string kind, long revision,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
        => SampleChunkCorrectionBoundReceipts.CommitAsync(fixture, mutation, kind, revision, originals);

    private static async Task RefuseAsync(SampleChunkCanonicalFixture fixture, OpenSampleChunkWindow mutation,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals, Func<Task> current)
    {
        var request = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition, [mutation]);
        var operation = fixture.Owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var failures = new List<Exception>();
        var raw = fixture.Raw();
        Result<object>? original = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = fixture.Owner.Database.ApplyEmbedded(operation, cancellationToken: default);
            original = actual;
            originals.Add((operation, actual));
            await Assert.That(SampleRollupWholeFlow.Outcome(actual)).IsEqualTo(
                SampleRollupWholeFlow.Outcome(Result<object>.Fail(Errors.Problem(ErrorCode.Conflict, ConflictDetail))));

        }, failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(fixture.Raw()).IsEqualTo(raw), failures);
        await ServerFailureObserver.ObserveAsync(current, failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(fixture.Owner.Store.Read(view =>
            view.ReadOwnedValue(SampleChunkKeys.Window(fixture.Owner.Partition, mutation.SeriesSet,
                mutation.SeriesId, mutation.WindowId)))).IsNull(), failures);
        if (original is { } retained)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                fixture.Owner.Database, fixture.Owner.Store, operation, retained), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
