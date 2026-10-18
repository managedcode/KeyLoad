using System.Text.Json;
using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkPendingBoundFlow
{
    private const string AppendKind = "appendSamples";
    private const string MergeKind = "mergeSampleChunkWindow";
    private const string Exhausted = "The native series chunk window exceeds its admitted bound.";

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SampleChunkCanonicalFixture(new()
            {
                MaximumPendingChunkWindows = SampleChunkPendingBoundProtocol.Pending,
                MaximumChunkWindows = SampleChunkPendingBoundProtocol.Windows
            });
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(SampleChunkCanonicalFixture fixture)
    {
        var trial = new SampleChunkPendingBoundTrial(fixture);
        var originals = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        await trial.SeedAsync(originals);
        await trial.RequireAsync(firstMerged: false, secondCorrected: false);
        await SampleChunkPendingWindowEnrollmentBound.RefuseAsync(fixture, trial, originals);
        await RefuseAsync(fixture, trial, originals);
        await SampleChunkCorrectionBoundReceipts.CommitAsync(fixture,
            new MergeSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                fixture.WindowId, SampleChunkPendingBoundProtocol.Corrected),
            MergeKind, SampleChunkPendingBoundProtocol.Merged, originals);
        var merge = originals[^1].Result.Get<CommitReceipt>();
        await Assert.That(merge.Token.Position).IsEqualTo(fixture.Owner.Store.Position);
        await trial.RequireAsync(firstMerged: true, secondCorrected: false);
        await SampleChunkCorrectionBoundReceipts.CommitAsync(fixture, SampleChunkPendingBoundTrial.Overflow,
            AppendKind, SampleChunkPendingBoundProtocol.FourthSequence, originals);
        var healthy = originals[^1].Result.Get<CommitReceipt>();
        await Assert.That(healthy.Token.Position > merge.Token.Position).IsTrue();
        await Assert.That(healthy.Token.Position).IsEqualTo(fixture.Owner.Store.Position);
        await trial.RequireAsync(firstMerged: true, secondCorrected: true);
        await SampleChunkPendingBoundCold.RunAsync(fixture, trial, originals);
    }

    private static async Task RefuseAsync(SampleChunkCanonicalFixture fixture, SampleChunkPendingBoundTrial trial,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> originals)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition, [SampleChunkPendingBoundTrial.Overflow]);
        var operation = fixture.Owner.Database.CreateCommand(OperationKind.Batch, command.CommandId, SampleChunkCanonicalFixture.Principal, default, JsonSerializer.Serialize(command, JsonDefaults.Options));
        var raw = fixture.Raw();
        var failures = new List<Exception>();
        Result<object>? rejected = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            rejected = fixture.Owner.Database.ApplyEmbedded(operation, cancellationToken: default);
            originals.Add((operation, rejected));
            var expected = Result<object>.Fail(Errors.Problem(ErrorCode.ResourceExhausted, Exhausted));
            await Assert.That(SampleRollupWholeFlow.Outcome(rejected)).IsEqualTo(SampleRollupWholeFlow.Outcome(expected));
            await Assert.That(Errors.Code(rejected.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);

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
}
