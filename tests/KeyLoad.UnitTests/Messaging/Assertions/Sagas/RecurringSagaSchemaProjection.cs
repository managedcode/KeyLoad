using KeyLoad.Server;
using KeyLoad.UnitTests.Authorization;
using static KeyLoad.UnitTests.Messaging.RecurringSagaSchemaProtocol;

namespace KeyLoad.UnitTests.Messaging;

internal static class RecurringSagaSchemaProjection
{
    internal static async Task AllAsync(SafeProjectorOperationDatabase owner, RecurringSagaSchemaState state,
        bool bodySchema, bool headerSchema)
    {
        var fields = new List<string>();
        if (bodySchema)
        {
            fields.Add("payload:/secret");
        }
        if (headerSchema)
        {
            fields.Add("headers:/secret");
        }
        var definition = state.Definition with
        {
            PayloadJson = SafeBody,
            HeadersJson = SafeHeaders
        };
        var schedule = new RecurringScheduleInspection(definition, One, One, Zero, false, true, [.. fields]);
        var saga = new SagaInspection(state.Definition.Lane, SagaId, One, SagaPhase.Waiting,
            SafeBody, null, true, bodySchema ? ["state:/secret"] : []);
        await ObserveAsync(owner, state, async () => await SafeProjectorOperationSeed.EqualAsync(
            owner.Database.InspectRecurringSchedule(Reader, definition.Lane, ScheduleId, owner.CallerToken), schedule));
        await ObserveAsync(owner, state, async () => await SafeProjectorOperationSeed.EqualAsync(
            owner.Database.InspectSaga(Reader, definition.Lane, SagaId, owner.CallerToken), saga));
        await ObserveAsync(owner, state, async () => await DeniedAsync(() =>
            owner.Database.InspectRecurringSchedule(Denied, definition.Lane, ScheduleId, owner.CallerToken)));
        await ObserveAsync(owner, state, async () => await DeniedAsync(() =>
            owner.Database.InspectSaga(Denied, definition.Lane, SagaId, owner.CallerToken)));
    }

    internal static async Task CancelledAsync(SafeProjectorOperationDatabase owner, RecurringSagaSchemaState state)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(owner.CallerToken);
        await cancelled.CancelAsync();
        await ObserveAsync(owner, state, async () =>
        {
            var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
                owner.Database.InspectRecurringSchedule(Reader, state.Definition.Lane, ScheduleId, cancelled.Token));
            await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        });
        await ObserveAsync(owner, state, async () =>
        {
            var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
                owner.Database.InspectSaga(Reader, state.Definition.Lane, SagaId, cancelled.Token));
            await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        });
        await AllAsync(owner, state, bodySchema: false, headerSchema: false);
    }

    internal static async Task ObserveAsync(SafeProjectorOperationDatabase owner, RecurringSagaSchemaState state,
        Func<Task> operation)
    {
        var identity = OrleansSerialization.Serialize(owner.Store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RecurringSagaSchemaCanonical.CurrentAsync(owner, state), failures);
        await ServerFailureObserver.ObserveAsync(() => SafeProjectorOperationImages.ObserveAsync(owner, operation), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(OrleansSerialization.Serialize(owner.Store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => RecurringSagaSchemaCanonical.CurrentAsync(owner, state), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DeniedAsync<T>(Func<T> operation)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => operation());
        await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(error.Message).IsEqualTo(DeniedDetail);
    }
}
