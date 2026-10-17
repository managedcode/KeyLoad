namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CommandOutcomeAuthorizationOrderTests
{
    [Test]
    public Task DeniedOperationDoesNotValidateCorruptCurrentOutcomeBeforeAuthorization()
        => CommandOutcomeAuthorizationOrderScenario.RunAsync();
}
