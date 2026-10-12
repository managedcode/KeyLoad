using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLeaseExpiryEqualityAssertions
{
    internal static async Task RefusalAsync(OperationResult result, ErrorCode code)
    {
        await Assert.That(result.Error).IsEqualTo(code);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }

    internal static async Task ColdImageAsync(ZoneTreeStore store, string[] image, long position, byte[] identity)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(NativeSerialization.Serialize(store.Identity)).IsEquivalentTo(identity, CollectionOrdering.Matching);
    }

    internal static async Task DeliveryAsync(Delivery delivery, string id, string payload, string headers)
    {
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(headers);
    }
}
