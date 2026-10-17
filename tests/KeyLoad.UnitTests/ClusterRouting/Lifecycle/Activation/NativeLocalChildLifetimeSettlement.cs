using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeSettlement
{
    internal static async Task HeldRetryAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeSeeded state, NativeLocalChildLifetimeOperation operation,
        NativeLocalChildLifetimeInput original)
    {
        var input = NativeLocalChildLifetimePayload.Sign(operation, original.Request, Guid.NewGuid(), P.Root);
        var reservation = session.Reserve(operation, state, input);
        var lifetime = input.Request.Coverage.Lifetimes.Single();
        await NativeLocalChildLifetimeAssertions.CopiedAsync(reservation, operation, lifetime);
        var child = NativeLocalChildLifetimeExecution.Consume(state, operation, input);
        await Assert.That(child).IsSameReferenceAs(reservation.Child);
        var producer = NativeLocalChildLifetimeExecution.HeldProducerAsync(session, state, operation, input, child);
        session.Producers.Add(producer);
        await NativeLocalChildLifetimeExecution.AwaitReadyAsync(session, producer, operation.Token);
        var preparing = operation.Parent.PrepareCompletionAsync();
        var completion = operation.Parent.CompleteAsync(null, null);
        await Assert.That<object>(completion).IsSameReferenceAs(operation.Parent.Settlement);
        await Assert.That(preparing.IsCompleted).IsFalse();
        await Assert.That(completion.IsCompleted).IsFalse();
        await Assert.That(producer.IsCompleted).IsFalse();
        session.ReleaseProducer.TrySetResult();
        var result = await producer;
        await NativeLocalChildLifetimeAssertions.QueryAsync(state, input, result);
        await Assert.That(completion.IsCompleted).IsFalse();
        await Assert.That(child.Settlement.IsCompleted).IsFalse();
        var childOutcome = await child.CompleteAsync(null, null);
        await Assert.That<object>(child.Settlement).IsSameReferenceAs(reservation.Settlement);
        await Assert.That(completion.IsCompleted).IsFalse();
        var dispatch = reservation.CompleteDispatchAsync(null, []);
        var report = await dispatch;
        await Assert.That<object>(reservation.CompleteDispatchAsync(null, [])).IsSameReferenceAs(dispatch);
        await Assert.That(report.ChildOutcome).IsSameReferenceAs(childOutcome);
        await Assert.That(report.OriginalFailure).IsNull();
        await Assert.That(report.CleanupFailures.IsEmpty).IsTrue();
        await preparing;
        var parentOutcome = await completion;
        await Assert.That(await operation.Parent.Settlement).IsSameReferenceAs(parentOutcome);
        await Assert.That(parentOutcome.OriginalFailure).IsNull();
        await Assert.That(parentOutcome.CleanupFailures.IsEmpty).IsTrue();
        await Assert.That(operation.Ledger.RetainedEncodedBytes).IsEqualTo(NativeSerialization.Measure(lifetime));
        await operation.Owner.DisposeAsync();
        await Assert.That(operation.Owner.IsJoined).IsTrue();
        await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(NativeLocalChildLifetimeProtocol.LifetimeCount);
        operation.Ledger.Dispose();
        _ = Assert.ThrowsExactly<ObjectDisposedException>(() => _ = operation.Ledger.RetainedEntries);
    }
}
