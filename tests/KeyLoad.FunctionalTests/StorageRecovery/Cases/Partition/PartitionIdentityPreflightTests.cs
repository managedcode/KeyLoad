using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PartitionIdentityPreflightTests
{
    [Test]
    [Arguments(StoreReaderContract.Unspecified)]
    [Arguments(PartitionIdentityProtocol.UnknownCapability)]
    public Task InvalidCurrentIdentityRejectsBeforeTreeRecoveryThenRepairContinuesCold(int capability)
        => PartitionIdentityPreflightTrial.RunAsync(capability,
            TestContext.Current!.Execution.CancellationToken);
}
