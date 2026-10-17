using System.Text.Json;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkPendingWindowEnrollmentBound
{
    private const string Exhausted = "The native series chunk window exceeds its admitted bound.";

    internal static async Task RefuseAsync(SampleChunkCanonicalFixture fixture, SampleChunkPendingBoundTrial trial,
        List<(ReplicatedOperation Operation, OperationResult Result)> originals)
    {
        var from = SampleChunkCanonicalFixture.Until.AddHours(SampleChunkPendingBoundProtocol.WindowHours);
        var until = from.AddHours(SampleChunkPendingBoundProtocol.WindowHours);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition,
            [new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                trial.ExcessWindow, from, until)]);
        var operation = new ReplicatedOperation(command.CommandId, OperationKind.Batch,
            SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var raw = fixture.Raw();
        OperationResult? rejected = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            rejected = fixture.Owner.Database.ApplyEmbedded(operation, cancellationToken: default);
            originals.Add((operation, rejected));
            var expected = new OperationResult(null, ErrorCode.ResourceExhausted, Exhausted);
            await Assert.That(SampleRollupWholeFlow.Outcome(rejected)).IsEqualTo(SampleRollupWholeFlow.Outcome(expected));
            await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(rejected.Json).IsNull();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(fixture.Raw()).IsEqualTo(raw), failures);
        await ServerFailureObserver.ObserveAsync(() =>
            trial.RequireAsync(firstMerged: false, secondCorrected: false), failures);
        if (rejected is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionBoundColdAssertions.ReplayAsync(
                fixture.Owner.Database, fixture.Owner.Store, operation, rejected), failures);
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
