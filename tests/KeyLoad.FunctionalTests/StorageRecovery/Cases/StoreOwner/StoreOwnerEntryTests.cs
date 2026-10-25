namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class StoreOwnerEntryTests
{
    [Test]
    public Task ExistingSymlinkOwnerRefusesBothOpenPathsThenOriginalStoreReopens()
        => StoreOwnerEntryFlow.RunAsync(StoreOwnerEntryFlow.SymlinkAsync);

    [Test]
    public Task ExistingDirectoryOwnerRefusesBothOpenPathsThenOriginalStoreReopens()
        => StoreOwnerEntryFlow.RunAsync(StoreOwnerEntryFlow.DirectoryAsync);

    [Test]
    public Task ExistingOnlyMissingOwnerDoesNotCreateThenOriginalStoreReopens()
        => StoreOwnerEntryFlow.RunAsync(StoreOwnerEntryFlow.MissingAsync);

    [Test]
    public Task FreshAndPreexistingEmptyOwnerRetainIdentityAndExclusiveLock()
        => StoreOwnerEntryFlow.RunAsync(StoreOwnerEntryHealthy.FreshAsync);
}
