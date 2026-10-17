using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextParentBoundaryAssertions
{
    private const string Unsupported = "The operation is unsupported.";
    internal static async Task RejectAsync(TestDatabase database, TextIndexMaintenanceRequest request)
    {
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.CreateNativeOperation(
            OperationKind.MaintainTextIndex, request.CommandId, NativeTextMaintenanceTestValues.Principal,
            database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(error.Message).IsEqualTo(Unsupported);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
