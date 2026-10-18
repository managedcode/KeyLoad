using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryOperations
{
    internal static EnqueueMessage Literal(string id, string? key = QueueOrderedRetryProtocol.SharedKey)
        => new(QueueOrderedRetryProtocol.Queue, id, QueueOrderedRetryProtocol.Payload, QueueOrderedRetryProtocol.Headers, OrderingKey: key);
    internal static ReceiveResult Receive(DatabaseEngine database, QueueOrderedRetryState state)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, MaxMessages: QueueOrderedRetryProtocol.One);
        return state.Execute(database, OperationKind.Receive, request, request.RequestId).Get<ReceiveResult>();
    }
    internal static async Task<Delivery> ClaimAsync(DatabaseEngine database, QueueOrderedRetryState state, string id)
    {
        var actual = await Assert.That(Receive(database, state).Deliveries).HasSingleItem();
        await Assert.That(actual.Id).IsEqualTo(id);
        await Assert.That(actual.PayloadJson).IsEqualTo(QueueOrderedRetryProtocol.Payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(QueueOrderedRetryProtocol.Headers);
        return actual;
    }
    internal static Result<object> Complete(DatabaseEngine database, QueueOrderedRetryState state, Delivery lease, DeliveryAction action)
    {
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, lease.Token, action);
        return state.Execute(database, OperationKind.Delivery, request, request.CommandId);
    }
    internal static async Task ReplayAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        foreach (var item in state.Outcomes.Where(item => item.Operation.Value!.Kind != OperationKind.Receive))
        {
            await ReplayResultAsync(database, item.Operation, item.Result);
        }
    }
    private static async Task ReplayResultAsync(DatabaseEngine database, Command<DatabaseCommand> operation, Result<object> original)
    {
        var actual = database.Apply(operation);
        if (Errors.Code(original.Problem) is not null)
        {
            await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(Errors.Code(original.Problem));
            await Assert.That(actual.Problem?.Detail).IsEqualTo(original.Problem?.Detail);
            await Assert.That(actual.Json).IsEqualTo(original.Json);
            await Assert.That(actual.Value).IsNull();
            return;
        }
        switch (operation.Value!.Kind)
        {
            case OperationKind.ConfigureResource:
                await NativeReplayResultAssertions.Same<ResourceDefinition>(actual, original);
                break;
            case OperationKind.ConfigurePrincipal:
                await NativeReplayResultAssertions.Same<PrincipalRecord>(actual, original);
                break;
            case OperationKind.SetDispatch:
                await DispatchReplayAsync(actual, original);
                break;
            default:
                await NativeReplayResultAssertions.Same<CommitReceipt>(actual, original);
                break;
        }
    }
    private static async Task DispatchReplayAsync(Result<object> actual, Result<object> expected)
    {
        await Assert.That(actual.Json).IsEqualTo(expected.Json);
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(Errors.Code(expected.Problem));
        await Assert.That(actual.Problem?.Detail).IsEqualTo(expected.Problem?.Detail);
        await Assert.That(actual.Value?.GetType()).IsEqualTo(typeof(bool));
        await Assert.That(expected.Value?.GetType()).IsEqualTo(typeof(bool));
        await Assert.That(actual.Get<bool>()).IsEqualTo(expected.Get<bool>());
    }
}
