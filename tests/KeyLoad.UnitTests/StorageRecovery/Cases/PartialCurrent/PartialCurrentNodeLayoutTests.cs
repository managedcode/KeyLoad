namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartialCurrentNodeLayoutTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task GenuineCurrentNativePrefixAdmitsMissingCompanionThenExclusiveWriteAndTwoColdOwners(bool replicaPrefix)
        => PartialCurrentNodeLayoutFlow.RunAsync(replicaPrefix);
}
