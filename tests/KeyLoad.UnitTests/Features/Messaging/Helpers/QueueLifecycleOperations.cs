using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleOperations
{
    internal static async Task<Delivery> ClaimAsync(DatabaseEngine database, QueueLifecycleTestState state, string id)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueLifecycleTestProtocol.LeaseSeconds);
        var result = state.Execute(database, OperationKind.Receive, request, request.RequestId).Get<ReceiveResult>();
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecycleTestProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecycleTestProtocol.Headers);
        return delivery;
    }

    internal static OperationResult Complete(DatabaseEngine database, QueueLifecycleTestState state, Delivery delivery, DeliveryAction action)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, action);
        return state.Execute(database, OperationKind.Delivery, command, command.CommandId);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, QueueLifecycleTestState state)
    {
        foreach (var (operation, result) in state.Failures)
        {
            var actual = state.SubmitOriginal(database, operation);
            await Assert.That(actual.Error).IsEqualTo(result.Error);
            await Assert.That(actual.SafeDetail).IsEqualTo(result.SafeDetail);
            await Assert.That(actual.Json).IsEqualTo(result.Json);
            await Assert.That(actual.NativeValue).IsNull();
        }
        foreach (var (operation, result) in state.Successes.Where(item => item.Operation.Kind != OperationKind.Receive))
        {
            var actual = state.SubmitOriginal(database, operation);
            if (operation.Kind == OperationKind.SetDispatch)
            { await DispatchReplayAsync(actual, result); }
            else if (operation.Kind == OperationKind.ConfigurePrincipal)
            { await NativeReplayResultAssertions.Same<PrincipalRecord>(actual, result); }
            else
            { await NativeReplayResultAssertions.Same<CommitReceipt>(actual, result); }
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
