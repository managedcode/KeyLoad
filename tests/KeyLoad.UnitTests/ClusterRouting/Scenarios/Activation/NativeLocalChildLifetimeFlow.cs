using KeyLoad.Clustering;
using KeyLoad.Server;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeFlow
{
    internal static async Task ExecuteAsync(CancellationToken token)
    {
        var session = new NativeLocalChildLifetimeSession();
        var failures = new List<Exception>();
        CancellationTokenSource? bound = null;
        CancellationTokenSource? linked = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsHostedRuntimeFixture();
            await using var other = new RequestCqrsHostedRuntimeFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                bound = new CancellationTokenSource(new NativeRuntimeTestOptions().StartupTimeout, TimeProvider.System);
                linked = CancellationTokenSource.CreateLinkedTokenSource(token, bound.Token);
                await fixture.InitializeAsync(linked.Token);
                await other.InitializeAsync(linked.Token);
                var seeded = await NativeLocalChildLifetimeSeed.CreateAsync(fixture, other, linked.Token);
                var operation = session.Initialize(seeded, linked.Token);
                await ExerciseAsync(session, seeded, operation);
                await NativeLocalChildLifetimeConservation.VerifyAsync(fixture, seeded, linked.Token);
            }, failures);
            await ServerFailureObserver.ObserveAsync(() =>
                NativeLocalChildLifetimeCleanup.JoinAsync(session, failures), failures);
        }, failures);
        ServerFailureObserver.Observe(() => linked?.Dispose(), failures);
        ServerFailureObserver.Observe(() => bound?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeSeeded state, NativeLocalChildLifetimeOperation operation)
    {
        var input = NativeLocalChildLifetimePayload.Create(state, operation);
        var changed = NativeLocalChildLifetimePayload.Create(state, operation, K.ChangedLimit);
        await Assert.That(input.Verified.Envelope.Purpose).IsEqualTo(QueryLifetimeReadProtocol.Purpose);
        await Assert.That(input.Verified.Envelope.ReadKind).IsEqualTo(GrainReadKind.PartitionQueryLeafV2);
        var lifetime = input.Request.Coverage.Lifetimes.Single();
        await NativeLocalChildLifetimeAssertions.RetentionAsync(operation, lifetime);
        await ClosedUndispatchedAsync(session, state, operation, input);
        var reservation = session.Reserve(operation, state, input);
        await NativeLocalChildLifetimeAssertions.CopiedAsync(reservation, operation, lifetime);
        await NativeLocalChildLifetimeRefusals.ExerciseAsync(state, operation, input, changed);
        var child = NativeLocalChildLifetimeExecution.Consume(state, operation, input);
        await Assert.That(child).IsSameReferenceAs(reservation.Child);
        await NativeLocalChildLifetimeAssertions.QueryAsync(state, input,
            NativeLocalChildLifetimeExecution.Read(state, operation, input, child));
        await NativeLocalChildLifetimeAssertions.RefusedAsync(() =>
            NativeLocalChildLifetimeExecution.Consume(state, operation, input));
        var outcome = await child.CompleteAsync(null, null);
        var dispatch = await reservation.CompleteDispatchAsync(null, []);
        await Assert.That(dispatch.ChildOutcome).IsSameReferenceAs(outcome);
        await Assert.That(dispatch.CleanupFailures.IsEmpty).IsTrue();
        await NativeLocalChildLifetimeAssertions.RetentionAsync(operation, lifetime);
        await NativeLocalChildLifetimeSettlement.HeldRetryAsync(session, state, operation, input);
    }

    private static async Task ClosedUndispatchedAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeSeeded state, NativeLocalChildLifetimeOperation operation,
        NativeLocalChildLifetimeInput original)
    {
        var input = NativeLocalChildLifetimePayload.Sign(operation, original.Request, Guid.NewGuid(), P.Root);
        var reservation = session.Reserve(operation, state, input);
        await NativeLocalChildLifetimeAssertions.CopiedAsync(reservation, operation,
            original.Request.Coverage.Lifetimes.Single());
        var dispatch = reservation.CompleteDispatchAsync(null, []);
        var report = await dispatch;
        await Assert.That<object>(reservation.CompleteDispatchAsync(null, [])).IsSameReferenceAs(dispatch);
        await Assert.That(report.ChildOutcome).IsSameReferenceAs(await reservation.Settlement);
        await Assert.That(report.CleanupFailures.IsEmpty).IsTrue();
        await NativeLocalChildLifetimeAssertions.RefusedAsync(() =>
            NativeLocalChildLifetimeExecution.Consume(state, operation, input));
        await NativeLocalChildLifetimeAssertions.RetentionAsync(operation, original.Request.Coverage.Lifetimes.Single());
    }

}
