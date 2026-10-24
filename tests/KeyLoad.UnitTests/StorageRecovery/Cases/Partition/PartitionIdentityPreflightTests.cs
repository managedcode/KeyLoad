using KeyLoad.Storage;

namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class PartitionIdentityPreflightTests
{
    [Test]
    [Arguments(StoreReaderContract.Unspecified)]
    [Arguments(PartitionIdentityProtocol.UnknownCapability)]
    public Task InvalidCurrentIdentityRejectsBeforeTornTailRecoveryThenRepairContinuesCold(int capability)
        => PartitionIdentityPreflightTrial.RunAsync(capability,
            TestContext.Current!.Execution.CancellationToken);
}
