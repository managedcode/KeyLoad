namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class PreparationOwnershipTests
{
    [Test]
    public Task DiscardedPreparationNeverPublishesAndPreservesOriginalFailure()
        => PreparationOwnershipOwner.RunAsync(PreparationOwnershipFlow.DiscardAsync);

    [Test]
    public Task ForkSeesParentStageAndResetRestoresInheritedImage()
        => PreparationOwnershipOwner.RunAsync(PreparationOwnershipFlow.InheritedAsync);

    [Test]
    public Task PreparationEscapesAndForeignThreadsRefuseBeforeCallback()
        => PreparationOwnershipOwner.RunAsync(PreparationOwnershipFlow.LifetimeAsync);

    [Test]
    public Task NestedStorePreparationRefusesOwnedReadAndWriteGatesWithoutPublication()
        => PreparationOwnershipOwner.RunAsync(PreparationOwnershipFlow.NestedAsync);
}
