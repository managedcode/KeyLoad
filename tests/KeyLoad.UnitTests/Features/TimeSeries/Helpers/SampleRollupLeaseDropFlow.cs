using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleRollupLeaseDropFlow
{
    private const int ScopeSeconds = 5;
    private const int MaximumRecords = 4096;
    private const string Truncated = "The complete held rollup read exceeded its fixture bound.";
    private const string WrongSession = "The pending writer belongs to a different store opening.";
    internal static async Task<OperationResult> RunAsync(TestDatabase db, Guid id, DropSampleRollup drop)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ScopeSeconds), TimeProvider.System);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<SampleRollupHeldRead>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = db.Store.GateDiagnostics.SessionId;
        var before = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var reader = Task.Factory.StartNew(() => HoldRead(db, entered, release, scope.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<OperationResult>? writer = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var held = await entered.Task.WaitAsync(scope.Token);
                writer = Task.Factory.StartNew(() => Submit(db, id, drop, scope.Token), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
                var queued = await ObserveQueuedAsync(db.Store, session, scope.Token);
                await Assert.That(queued.WaitingWriters).IsGreaterThan(0);
                await SampleRollupWholeFlow.Literal(held.Result, 1, 6, 3, 12, 2, 6, 4);
                await Assert.That(held.CompleteImage).IsEqualTo(before);
                await Assert.That(held.Position).IsEqualTo(position);
                await Assert.That(db.Store.Position).IsEqualTo(position);
            }, failures);
        }
        finally { release.Set(); }
        await ServerFailureObserver.ObserveAsync(() => reader, failures);
        if (writer is { } owned)
        { await ServerFailureObserver.ObserveAsync(() => owned, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return await writer!;
    }
    private static SampleRollupHeldRead HoldRead(TestDatabase db, TaskCompletionSource<SampleRollupHeldRead> entered,
        ManualResetEventSlim release, CancellationToken cancellation)
    {
        try
        {
            return db.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(db.Database.OperationLimitsOptions, db.Database.EvaluationClock, cancellation);
            var request = new ReadSampleRollupRequest(db.Partition, SampleRollupWholeFlow.Set,
                SampleRollupWholeFlow.Series, SampleRollupWholeFlow.Start, SampleRollupWholeFlow.End);
            var result = SampleRollupReader.Read(db.Database, budget.CreateView(view), SampleRollupWholeFlow.Root, request, budget);
            budget.CheckResult(result);
            var page = view.Scan([], MaximumRecords);
            if (page.HasMore)
            { throw new InvalidOperationException(Truncated); }
            var bytes = JsonSerializer.Serialize(page.Records.Select(record =>
                new[] { Convert.ToHexString(record.Key.Span), Convert.ToHexString(record.Value.Span) }).ToArray());
            var held = new SampleRollupHeldRead(result, bytes, db.Store.Position);
            entered.TrySetResult(held);
            release.Wait(cancellation);
            return held;
        });
        }
        catch (Exception error) { entered.TrySetException(error); throw; }
    }
    private static OperationResult Submit(TestDatabase db, Guid id, DropSampleRollup drop, CancellationToken cancellation)
        => db.Database.ApplyEmbedded(new(id, OperationKind.Batch, SampleRollupWholeFlow.Root, default,
            JsonSerializer.Serialize(new CommandRequest(id, db.Partition, [drop]), JsonDefaults.Options)), cancellation);
    private static async Task<ZoneTreeGateSnapshot> ObserveQueuedAsync(ZoneTreeStore store, Guid session,
        CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var snapshot = store.GateDiagnostics;
            if (snapshot.SessionId != session)
            { throw new InvalidOperationException(WrongSession); }
            if (snapshot.WaitingWriters > 0)
            { return snapshot; }
            await Task.Yield();
        }
    }
}
