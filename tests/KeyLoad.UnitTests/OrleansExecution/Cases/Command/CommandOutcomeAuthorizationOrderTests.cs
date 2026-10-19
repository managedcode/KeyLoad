namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class CommandOutcomeAuthorizationOrderTests
{
    [Test]
    public Task DeniedOperationDoesNotValidateCorruptCurrentOutcomeBeforeAuthorization()
        => CommandOutcomeAuthorizationOrderScenario.RunAsync();
}
