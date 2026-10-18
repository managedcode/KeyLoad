using System.Text.Json;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkPendingWindowEnrollmentBound
{
    private const string Exhausted = "The native series chunk window exceeds its admitted bound.";

    internal static async Task RefuseAsync(SampleChunkCanonicalFixture fixture, SampleChunkPendingBoundTrial trial,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var from = SampleChunkCanonicalFixture.Until.AddHours(SampleChunkPendingBoundProtocol.WindowHours);
        var until = from.AddHours(SampleChunkPendingBoundProtocol.WindowHours);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                trial.ExcessWindow, from, until)]);
        var operation = fixture.Owner.Database.CreateCommand(OperationKind.Batch, command.CommandId, SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var raw = fixture.Raw();
        Result<object>? rejected = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = fixture.Owner.Database.ApplyEmbedded(operation, cancellationToken: default);
            rejected = actual;
            originals.Add((operation, actual));
            var expected = Result<object>.Fail(Errors.Problem(ErrorCode.ResourceExhausted, Exhausted));
            await Assert.That(SampleRollupWholeFlow.Outcome(actual)).IsEqualTo(SampleRollupWholeFlow.Outcome(expected));
            await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);

        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(fixture.Raw()).IsEqualTo(raw), failures);
        await ServerFailureObserver.ObserveAsync(() =>
            trial.RequireAsync(firstMerged: false, secondCorrected: false), failures);
        if (rejected is { } retained)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                fixture.Owner.Database, fixture.Owner.Store, operation, retained), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task AbsentAsync(ZoneTreeStore store, PartitionRef partition, Guid windowId)
    {
        var image = SampleChunkCorrectionBoundColdAssertions.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var key = SampleChunkKeys.Window(partition, SampleChunkCanonicalFixture.Set,
                SampleChunkCanonicalFixture.Series, windowId);
            var actual = store.Read(view => view.ReadOwnedValue(key));
            await Assert.That(actual).IsNull();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() =>
            SampleChunkCorrectionBoundColdAssertions.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
