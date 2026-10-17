using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.ClusterReplication.CoordinatorShutdownProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class CoordinatorShutdownFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        TestDatabase? db = null;
        CoordinatorShutdownAdmission? admission = null;
        var storage = new CoordinatorShutdownStorage();
        ServerFailureObserver.Observe(() => db = new(), failures);
        if (db is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                db.Configure(P.Collection, ResourceKind.Collection);
                db.Commit(new PutDocument(P.Collection, P.Original, P.Json));
                admission = new(db);
                await RefuseAsync(db, admission, token);
                await CoordinatorShutdownNativeAdmission.RunAsync(db, storage, token);
                await CoordinatorShutdownHealthy.RunAsync(db, storage, token);
            }, failures);
            if (admission is not null)
            {
                await admission.CleanupAsync(failures);
            }
            storage.Cleanup(db, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefuseAsync(TestDatabase db, CoordinatorShutdownAdmission admission, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var position = db.Store.Position;
        var active = admission.Enqueue(P.Interrupted);
        admission.Start();
        await admission.Entered.Task.WaitAsync(token);
        var queued = admission.Enqueue(P.Queued);
        await Assert.That(File.Exists(Path.Combine(db.Directory, P.MissingFile))).IsFalse();
        var shutdown = admission.Stop();
        await admission.Cancelled.Task.WaitAsync(token);
        await Assert.That(shutdown.IsCompleted).IsFalse();
        await Assert.That(admission.Worker!.IsCompleted).IsFalse();
        await Assert.That(admission.Exited.Task.IsCompleted).IsFalse();
        await Assert.That(admission.Governor.Snapshot().Commands).IsEqualTo(P.ActiveCommands);
        var queuedFailure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => queued.Completion.WaitAsync(token));
        admission.ExpectedQueued = queuedFailure;
        await Assert.That(queuedFailure!.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(queuedFailure.Message).IsEqualTo(P.QueuedDetail);
        await ReplicaTransportShutdownFlow.UnchangedAsync(db, image, position);
        using var cancelledWaiter = new CancellationTokenSource();
        await cancelledWaiter.CancelAsync().ConfigureAwait(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => shutdown.WaitAsync(cancelledWaiter.Token));
        await Assert.That(shutdown.IsCompleted).IsFalse();
        admission.Release();
        var failure = await Assert.ThrowsExactlyAsync<AggregateException>(() => shutdown.WaitAsync(token));
        await Assert.That(failure!.Flatten().InnerExceptions.Count).IsEqualTo(P.CallbackFailures);
        await Assert.That(failure.Flatten().InnerExceptions.Single()).IsSameReferenceAs(admission.CallbackFailure);
        await Assert.That(admission.CallbackFailure).IsNotNull();
        await Assert.That(admission.CallbackFailure!.FileName).IsEqualTo(Path.Combine(db.Directory, P.MissingFile));
        await Assert.That(admission.Exited.Task.IsCompleted).IsTrue();
        var activeFailure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => active.Completion.WaitAsync(token));
        admission.ExpectedActive = activeFailure;
        await Assert.That(activeFailure!.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(activeFailure.Message).IsEqualTo(P.InterruptedDetail);
        await Assert.That(admission.Governor.Snapshot()).IsEqualTo(P.EmptyAdmission);
        var disposal = admission.Dispose();
        var disposing = await Assert.ThrowsExactlyAsync<AggregateException>(() => disposal.WaitAsync(token));
        admission.ExpectedDisposal = disposing;
        await Assert.That(disposing).IsSameReferenceAs(failure);
        await Assert.That(admission.Dispose()).IsSameReferenceAs(disposal);
        await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => _ = admission.Lifetime.Token)).IsNotNull();
        await ReplicaTransportShutdownFlow.UnchangedAsync(db, image, position);
    }
}
