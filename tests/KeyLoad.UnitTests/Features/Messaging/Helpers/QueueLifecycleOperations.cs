using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleOperations
{
    internal static async Task<Delivery> ClaimAsync(DatabaseEngine database, QueueLifecycleTestState state, string id)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueLifecycleTestProtocol.LeaseSeconds);
        var original = state.Execute(database, OperationKind.Receive, request, request.RequestId);
        var result = original.Get<ReceiveResult>();
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecycleTestProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecycleTestProtocol.Headers);
        var before = QueueLifecycleReplayCutAssertions.Capture(database.Store);
        var operation = state.Successes.Last().Operation;
        await QueueReceiveReplayAssertions.SameAsync(database.Store, state.Partition, operation, original, true,
            () => state.SubmitOriginal(database, operation));
        await QueueLifecycleReplayCutAssertions.SameAsync(database.Store, before);
        return delivery;
    }

    internal static OperationResult Complete(DatabaseEngine database, QueueLifecycleTestState state, Delivery delivery, DeliveryAction action)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, action);
        return state.Execute(database, OperationKind.Delivery, command, command.CommandId);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, QueueLifecycleTestState state,
        string? currentMessageId = null)
    {
        var before = QueueLifecycleReplayCutAssertions.Capture(database.Store);
        foreach (var (operation, result) in state.Failures)
        {
            var actual = state.SubmitOriginal(database, operation);
            await Assert.That(actual.Error).IsEqualTo(result.Error);
            await Assert.That(actual.SafeDetail).IsEqualTo(result.SafeDetail);
            await Assert.That(actual.Json).IsEqualTo(result.Json);
            await Assert.That(actual.NativeValue).IsNull();
            await QueueLifecycleReplayCutAssertions.SameAsync(database.Store, before);
        }
        foreach (var (operation, result) in state.Successes)
        {
            if (operation.Kind == OperationKind.Receive)
            {
                var current = result.Get<ReceiveResult>().Deliveries.All(item => item.Id == currentMessageId);
                await QueueReceiveReplayAssertions.SameAsync(database.Store, state.Partition, operation, result, current,
                    () => state.SubmitOriginal(database, operation));
                await QueueLifecycleReplayCutAssertions.SameAsync(database.Store, before);
                continue;
            }
            var actual = state.SubmitOriginal(database, operation);
            if (operation.Kind == OperationKind.SetDispatch)
            { await DispatchReplayAsync(actual, result); }
            else if (operation.Kind == OperationKind.ConfigurePrincipal)
            { await NativeReplayResultAssertions.Same<PrincipalRecord>(actual, result); }
            else
            { await NativeReplayResultAssertions.Same<CommitReceipt>(actual, result); }
            await QueueLifecycleReplayCutAssertions.SameAsync(database.Store, before);
        }
    }

    private static async Task DispatchReplayAsync(OperationResult actual, OperationResult expected)
    {
        await Assert.That(actual.Json).IsEqualTo(expected.Json);
        await Assert.That(actual.Error).IsEqualTo(expected.Error);
        await Assert.That(actual.SafeDetail).IsEqualTo(expected.SafeDetail);
        await Assert.That(actual.NativeValue?.GetType()).IsEqualTo(typeof(bool));
        await Assert.That(expected.NativeValue?.GetType()).IsEqualTo(typeof(bool));
        await Assert.That(actual.Get<bool>()).IsEqualTo(expected.Get<bool>());
    }
}
