namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativePreparationOwnershipTests
{
    [Test]
    public Task DiscardedNativePreparationNeverPublishesAndPreservesOriginalFailure()
        => NativePreparationOwnershipOwner.RunAsync(NativePreparationOwnershipFlow.DiscardAsync);

    [Test]
    public Task NativeForkSeesParentStageAndResetRestoresInheritedImage()
        => NativePreparationOwnershipOwner.RunAsync(NativePreparationOwnershipFlow.InheritedAsync);

    [Test]
    public Task NativePreparationEscapesAndForeignThreadsRefuseBeforeCallback()
        => NativePreparationOwnershipOwner.RunAsync(NativePreparationOwnershipFlow.LifetimeAsync);

    [Test]
    public Task NestedStorePreparationRefusesOwnedReadAndWriteGatesWithoutPublication()
        => NativePreparationOwnershipOwner.RunAsync(NativePreparationOwnershipFlow.NestedAsync);
}
