namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PartialCurrentNodeLayoutTests
{
    [Test]
    public Task GenuineCurrentCanonicalPrefixRetainsIdentityThenExclusiveWriteAndTwoColdOwners()
        => PartialCurrentNodeLayoutFlow.RunAsync();
}
