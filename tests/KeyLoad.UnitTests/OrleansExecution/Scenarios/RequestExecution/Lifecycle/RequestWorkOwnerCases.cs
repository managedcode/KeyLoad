using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class RequestWorkOwnerCases
{
    private const int RequestProducerLimit = 64;
    private const int TotalWorkLimit = 128;
    private const int OtherCapabilityCount = TotalWorkLimit - RequestProducerLimit;
    private static readonly TimeSpan DrainBound = TimeSpan.FromSeconds(10);

    internal static async Task AssertCapacityAndReleaseAsync()
    {
        await using var owner = new RequestWorkOwner(UnitRoutingOptions.Routing());
        var leases = new List<IDisposable>();
        try
        {
            var emptyIdentity = AssertCapacityFailure(
                owner, Guid.Empty, RequestWorkKind.RequestProducer);
            await Assert.That(emptyIdentity.Code).IsEqualTo(ErrorCode.Validation);
            var unknownKind = AssertCapacityFailure(
                owner, Guid.NewGuid(), (RequestWorkKind)255);
            await Assert.That(unknownKind.Code).IsEqualTo(ErrorCode.Validation);

            var firstId = Guid.NewGuid();
            leases.Add(owner.Acquire(firstId, RequestWorkKind.RequestProducer));
            AddLeases(owner, RequestWorkKind.RequestProducer, RequestProducerLimit - 1, leases);
            var duplicate = AssertCapacityFailure(owner, firstId, RequestWorkKind.RequestProducer);
            await Assert.That(duplicate.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            var producerExcess = AssertCapacityFailure(owner, Guid.NewGuid(), RequestWorkKind.RequestProducer);
            await Assert.That(producerExcess.Code).IsEqualTo(ErrorCode.ResourceExhausted);

            AddLeases(owner, RequestWorkKind.ReadCapability, OtherCapabilityCount - 1, leases);
            AddLeases(owner, RequestWorkKind.CommandCapability, 1, leases);
            var totalExcess = AssertCapacityFailure(owner, Guid.NewGuid(), RequestWorkKind.CommandCapability);
            await Assert.That(totalExcess.Code).IsEqualTo(ErrorCode.ResourceExhausted);

            var released = leases[0];
            leases.RemoveAt(0);
            released.Dispose();
            released.Dispose();
            leases.Add(owner.Acquire(Guid.NewGuid(), RequestWorkKind.RequestProducer));
            var doubleReleaseExcess = AssertCapacityFailure(
                owner, Guid.NewGuid(), RequestWorkKind.CommandCapability);
            await Assert.That(doubleReleaseExcess.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        finally
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }
        }
    }

    internal static async Task AssertDrainAndClosedAdmissionAsync()
    {
        var owner = new RequestWorkOwner(UnitRoutingOptions.Routing());
        var lease = owner.Acquire(Guid.NewGuid(), RequestWorkKind.ReadCapability);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = owner.ShutdownToken.Register(() => cancellationObserved.TrySetResult());
        Task? firstDrain = null;
        await CqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            try
            {
                firstDrain = owner.DrainAsync();
                var secondDrain = owner.DrainAsync();
                await Assert.That(secondDrain).IsSameReferenceAs(firstDrain);
                await cancellationObserved.Task.WaitAsync(DrainBound, TimeProvider.System);
                await Assert.That(owner.ShutdownToken.IsCancellationRequested).IsTrue();
                await Assert.That(owner.IsJoined).IsFalse();
                await Assert.That(firstDrain.IsCompleted).IsFalse();
                var closed = Assert.ThrowsExactly<KeyLoadException>(() =>
                    owner.Acquire(Guid.NewGuid(), RequestWorkKind.CommandCapability));
                await Assert.That(closed.Code).IsEqualTo(ErrorCode.OwnershipLost);
            }
            finally
            {
                await lease.DisposeAsync();
            }
        }, () => FinishDrainAsync(owner, firstDrain, cancellationRegistration));

        await Assert.That(owner.IsJoined).IsTrue();
        await Assert.That(owner.DrainAsync()).IsSameReferenceAs(firstDrain!);
        await owner.DisposeAsync().AsTask().WaitAsync(DrainBound, TimeProvider.System);
    }

    internal static async Task AssertCallbackFailureJoinsLiveLeaseAsync()
    {
        var owner = new RequestWorkOwner(UnitRoutingOptions.Routing());
        var lease = owner.Acquire(Guid.NewGuid(), RequestWorkKind.ReadCapability);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFailure = new InvalidOperationException("The owner shutdown callback failed.");
        var registration = owner.ShutdownToken.Register(() =>
        {
            callbackEntered.TrySetResult();
            throw callbackFailure;
        });
        Task? drain = null;
        Task? disposal = null;
        Task? repeatedDisposal = null;
        var drainFailures = new List<Exception>();
        var disposalFailures = new List<Exception>();
        await CqrsTestSupport.RunWithCleanupAsync(async () =>
        {
            try
            {
                drain = owner.DrainAsync();
                await Assert.That(owner.DrainAsync()).IsSameReferenceAs(drain);
                disposal = owner.DisposeAsync().AsTask();
                repeatedDisposal = owner.DisposeAsync().AsTask();
                await Assert.That(repeatedDisposal).IsSameReferenceAs(disposal);
                await callbackEntered.Task.WaitAsync(DrainBound, TimeProvider.System);
                await registration.DisposeAsync();
                await Assert.That(drain.IsCompleted).IsFalse();
                await Assert.That(disposal.IsCompleted).IsFalse();
                await Assert.That(owner.IsJoined).IsFalse();
            }
            finally
            {
                await lease.DisposeAsync();
            }
        }, () => FinishCallbackFailureAsync(
            owner, drain, disposal, registration, drainFailures, disposalFailures));

        await Assert.That(owner.IsJoined).IsTrue();
        await RequestWorkKernelCleanup.AssertContainsFailureAsync(drainFailures, callbackFailure);
        await RequestWorkKernelCleanup.AssertContainsFailureAsync(disposalFailures, callbackFailure);
    }

    private static void AddLeases(RequestWorkOwner owner, RequestWorkKind kind,
        int count, List<IDisposable> leases)
    {
        for (var index = 0; index < count; index++)
        {
            leases.Add(owner.Acquire(Guid.NewGuid(), kind));
        }
    }

    private static KeyLoadException AssertCapacityFailure(
        RequestWorkOwner owner, Guid requestId, RequestWorkKind kind)
        => Assert.ThrowsExactly<KeyLoadException>(() => owner.Acquire(requestId, kind));

    private static async Task FinishDrainAsync(RequestWorkOwner owner, Task? drain,
        CancellationTokenRegistration registration)
    {
        var failures = new List<Exception>();
        try
        {
            if (drain is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => drain, failures);
            }
        }
        finally
        {
            await ServerFailureObserver.ObserveAsync(() => registration.DisposeAsync().AsTask(), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task FinishCallbackFailureAsync(RequestWorkOwner owner, Task? drain,
        Task? disposal, CancellationTokenRegistration registration, List<Exception> drainFailures,
        List<Exception> disposalFailures)
    {
        var cleanupFailures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(() => drain ?? owner.DrainAsync(), drainFailures);
        }
        finally
        {
            await ServerFailureObserver.ObserveAsync(() => registration.DisposeAsync().AsTask(), cleanupFailures);
        }
        await ServerFailureObserver.ObserveAsync(
            () => disposal ?? owner.DisposeAsync().AsTask(), disposalFailures);
        ServerFailureObserver.ThrowIfAny(cleanupFailures);
    }
}
