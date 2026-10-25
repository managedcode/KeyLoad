namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CommandOutcomeAuthorizationOrderTests
{
    [Test]
    public Task DeniedOperationDoesNotValidateCorruptCurrentOutcomeBeforeAuthorization()
        => CommandOutcomeAuthorizationOrderScenario.RunAsync();
}
