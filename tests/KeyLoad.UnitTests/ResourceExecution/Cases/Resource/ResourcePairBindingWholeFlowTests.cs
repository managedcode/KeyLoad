namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ResourcePairBindingWholeFlowTests
{
    [Test]
    [Arguments(ResourcePairBindingFault.MissingLifetime)]
    [Arguments(ResourcePairBindingFault.MalformedLifetime)]
    [Arguments(ResourcePairBindingFault.ConflictingLifetime)]
    [Arguments(ResourcePairBindingFault.MissingDefinition)]
    public Task NativeMixedModelsBlobAndSqlRefuseDamagedResourcePairsThenRecoverSameEntriesAcrossTwoColdOwners(ResourcePairBindingFault fault)
        => ResourceIncarnationOwner.RunAsync(source => ResourcePairBindingFlow.RunAsync(new(source, fault),
            TestContext.Current!.Execution.CancellationToken));
}
