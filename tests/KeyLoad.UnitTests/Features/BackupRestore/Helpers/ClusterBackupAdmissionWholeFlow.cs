using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class ClusterBackupAdmissionWholeFlow
{
    internal static async Task RequireAsync(bool shutdown, CancellationToken ct)
    {
        await using var fixture = new ClusterBackupAdmissionNativeFixture();
        var original = fixture.ReadCompleteImage();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var nativeGate = Task.Run(() => fixture.Host.Database.Store.Commit((_, _) =>
        {
            entered.SetResult();
            release.Wait(ct);
            return true;
        }), ct);
        var accepted = new List<Task<ClusterBackupOwnerReceipt>>();
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(() => RequireActualAsync(fixture, original, entered,
                release, nativeGate, accepted, shutdown, ct), failures);
        }
        finally
        {
            release.Set();
            await ServerFailureObserver.ObserveAsync(() => nativeGate, failures);
            foreach (var producer in accepted)
            { await ObserveSettledOriginalAsync(producer, failures); }

        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireActualAsync(ClusterBackupAdmissionNativeFixture fixture,
        byte[] original, TaskCompletionSource entered, ManualResetEventSlim release, Task nativeGate,
        List<Task<ClusterBackupOwnerReceipt>> accepted, bool shutdown, CancellationToken ct)
    {
        Task? stopping = null;
        await entered.Task.WaitAsync(ct);
        for (var index = 0; index < ClusterBackupAdmissionNativeFixture.AcceptedCaptures; index++)
        { accepted.Add(fixture.Capture(ct)); }
        var overload = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Capture(ct));
        await Assert.That(overload.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var ordinary = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Administration.BackupAsync(ct));
        await Assert.That(ordinary.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        if (shutdown)
        {
            stopping = fixture.Administration.DisposeAsync().AsTask();
            await Assert.That(stopping.IsCompleted).IsFalse();
            var closed = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Capture(ct));
            await Assert.That(closed.Code).IsEqualTo(ErrorCode.OwnershipLost);
        }
        release.Set();
        await nativeGate;
        foreach (var producer in accepted)
        {
            // The supporting owner deliberately has no registered RF3 owner; no cut is fabricated.
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => producer);
            ArgumentNullException.ThrowIfNull(failure);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        }
        await Assert.That(fixture.ReadCompleteImage()).IsEquivalentTo(original);
        if (stopping is null)
        { await fixture.RequireHealthyOrdinaryBackupAsync(original, ct); }
        stopping ??= fixture.Administration.DisposeAsync().AsTask();
        var shutdownFailures = await Assert.ThrowsExactlyAsync<AggregateException>(() => stopping);
        ArgumentNullException.ThrowIfNull(shutdownFailures);
        fixture.ObservedShutdownFailure = shutdownFailures;
        await Assert.That(shutdownFailures.InnerExceptions.Count).IsEqualTo(accepted.Count);
        await Assert.That(shutdownFailures.InnerExceptions.All(error => error is KeyLoadException native
            && native.Code == ErrorCode.OwnershipLost)).IsTrue();
        foreach (var producer in accepted)
        {
            var originalFailure = producer.Exception!.InnerExceptions.Single();
            await Assert.That(shutdownFailures.InnerExceptions.Count(error => ReferenceEquals(error, originalFailure)))
                .IsEqualTo(accepted.Count(task => ReferenceEquals(task.Exception!.InnerExceptions.Single(), originalFailure)));
        }
        await fixture.RequireUnchangedAndColdAsync(original);
    }

    private static async Task ObserveSettledOriginalAsync(Task producer, List<Exception> cleanup)
    {
        await producer.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (producer.Exception is { } faults)
        {
            foreach (var failure in faults.InnerExceptions)
            {
                if (failure is KeyLoadException original && original.Code == ErrorCode.OwnershipLost)
                { continue; }
                cleanup.Add(failure);
            }
        }
        else if (producer.IsCanceled)
        { cleanup.Add(new TaskCanceledException(producer)); }
    }
}
