namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseDisposalOperation
{
    internal static async Task ExecuteAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeSeeded state, NativeLocalChildLifetimeOperation operation, Exception? original, Exception? cleanup, bool completionFirst)
    {
        var input = NativeLocalChildLifetimePayload.Create(state, operation);
        await NativeLocalChildLifetimeRefusals.ReceivingTenantAsync(session, state, operation, input);
        var reservation = session.Reserve(operation, state, input);
        var child = NativeLocalChildLifetimeExecution.Consume(state, operation, input);
        var producer = NativeLocalChildLifetimeExecution.HeldProducerAsync(session, state, operation, input, child);
        session.Producers.Add(producer);
        await NativeLocalChildLifetimeExecution.AwaitReadyAsync(session, producer, operation.Token);
        var parent = operation.Parent;
        var synchronous = Assert.ThrowsExactly<KeyLoadException>(parent.Dispose);
        await Assert.That(synchronous.Code).IsEqualTo(ErrorCode.OwnershipLost);
        var disposal = NativeBoundLeaseDisposalReports.Begin(parent, original, cleanup, completionFirst);
        parent.PrepareDisposal(new InvalidOperationException(nameof(ExecuteAsync)), new IOException(nameof(ExecuteAsync)));
        await Assert.That(parent.DisposeAsync().AsTask()).IsSameReferenceAs(disposal);
        var completion = parent.CompleteAsync(new InvalidOperationException(nameof(NativeBoundLeaseDisposalOperation)), null);
        await Assert.That<object>(completion).IsSameReferenceAs(parent.Settlement);
        await Assert.That(disposal).IsSameReferenceAs(parent.Settlement);
        await NativeRequestWorkKernelAssertions.AssertBothRunningAsync(disposal, producer);
        session.ReleaseProducer.TrySetResult();
        await NativeLocalChildLifetimeAssertions.QueryAsync(state, input, await producer.WaitAsync(operation.Token));
        await NativeRequestWorkKernelAssertions.AssertStillRunningAsync(disposal);
        var childDisposal = child.DisposeAsync().AsTask();
        await childDisposal.WaitAsync(operation.Token);
        await Assert.That(child.DisposeAsync().AsTask()).IsSameReferenceAs(childDisposal);
        await Assert.That(childDisposal).IsSameReferenceAs(child.Settlement);
        await NativeRequestWorkKernelAssertions.AssertStillRunningAsync(disposal);
        await NativeBoundLeaseDisposalReports.ChargedAsync(operation.Owner, parent.RequestId);
        var dispatch = reservation.CompleteDispatchAsync(null, []);
        var report = await dispatch.WaitAsync(operation.Token);
        await Assert.That(report.ChildOutcome).IsSameReferenceAs(await child.Settlement);
        await Assert.That<object>(reservation.CompleteDispatchAsync(null, [])).IsSameReferenceAs(dispatch);
        await Assert.That(report.CleanupFailures.IsEmpty).IsTrue();
        await disposal.WaitAsync(operation.Token);
        var outcome = await parent.Settlement;
        await Assert.That(outcome.OriginalFailure).IsSameReferenceAs(original);
        await NativeBoundLeaseDisposalReports.CleanupAsync(outcome, cleanup);
        await Assert.That(await completion).IsSameReferenceAs(outcome);
        await Assert.That(parent.DisposeAsync().AsTask()).IsSameReferenceAs(disposal);
        await NativeLocalChildLifetimeAssertions.RetentionAsync(operation, input.Request.Coverage.Lifetimes.Single());
    }
}
