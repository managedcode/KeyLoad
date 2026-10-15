using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryReceiveFence
{
    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        LocalInboxProcessingRecoveryState state)
    {
        var image = LocalInboxProcessingRecoveryImage.All(store);
        var position = store.Position;
        var refused = database.Apply(state.Receive);
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(refused.SafeDetail).IsEqualTo("The delivery lease is stale.");
        await Assert.That(refused.Json).IsNull();
        await Assert.That(refused.NativeValue).IsNull();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(LocalInboxProcessingRecoveryImage.All(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await Assert.That(NativeSerialization.Serialize(OutcomeStoreOracle.Read(store, state.Receive)))
            .IsEquivalentTo(NativeSerialization.Serialize(state.Received), CollectionOrdering.Matching);
    }
}
