using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryMissingChoiceRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, Delivery lease)
    {
        var request = new DeliveryCommand(Guid.NewGuid(), state.Lane, lease.Token, DeliveryAction.Nack);
        var unprepared = database.NormalizeOperation(new(request.CommandId, OperationKind.Delivery, state.Principal,
            state.Time, JsonSerializer.Serialize(request, JsonDefaults.Options)));
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var position = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(unprepared));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
    }
}
