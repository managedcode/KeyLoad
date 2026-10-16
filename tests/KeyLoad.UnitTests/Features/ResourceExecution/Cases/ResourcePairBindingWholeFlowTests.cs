namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ResourcePairBindingWholeFlowTests
{
    [Test]
    [Arguments(ResourcePairBindingFault.MissingLifetime)]
    [Arguments(ResourcePairBindingFault.MalformedLifetime)]
    [Arguments(ResourcePairBindingFault.ConflictingLifetime)]
    [Arguments(ResourcePairBindingFault.MissingDefinition)]
    public Task NativeMixedModelsBlobAndSqlRefuseDamagedResourcePairsThenRecoverSameEntriesAcrossTwoColdOwners(ResourcePairBindingFault fault)
        => ResourcePairBindingFixture.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
