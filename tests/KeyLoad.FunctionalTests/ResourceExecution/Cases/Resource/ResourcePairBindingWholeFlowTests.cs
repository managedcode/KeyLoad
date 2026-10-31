namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class ResourcePairBindingWholeFlowTests
{
    [Test]
    [Arguments(ResourcePairBindingFault.MissingLifetime)]
    [Arguments(ResourcePairBindingFault.MalformedLifetime)]
    [Arguments(ResourcePairBindingFault.MissingDefinition)]
    public Task MixedModelsAndSqlRefuseDamagedResourcePairsThenRecoverSameEntriesAcrossTwoColdOwners(ResourcePairBindingFault fault)
        => ResourceIncarnationOwner.RunAsync(source => ResourcePairBindingFlow.RunAsync(new(source, fault),
            TestContext.Current!.Execution.CancellationToken));
}
