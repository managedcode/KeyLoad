namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ResourceRetirementDropParentNativeTests
{
    [Test]
    public async Task NativeParentIntentRequiresOriginalOutcomeAndCannotCompleteBeforeRetirementAcrossTwoColdOwners()
        => await ResourceIncarnationOwner.RunAsync(ResourceRetirementDropParentNativeFlow.RunAsync);
}
