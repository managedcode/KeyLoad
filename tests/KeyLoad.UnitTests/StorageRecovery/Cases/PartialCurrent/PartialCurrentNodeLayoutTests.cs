namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartialCurrentNodeLayoutTests
{
    [Test]
    public Task GenuineCurrentCanonicalPrefixRetainsIdentityThenExclusiveWriteAndTwoColdOwners()
        => PartialCurrentNodeLayoutFlow.RunAsync();
}
