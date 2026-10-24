namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class CommandOutcomeAuthorizationOrderTests
{
    [Test]
    public Task DeniedOperationDoesNotValidateCorruptCurrentOutcomeBeforeAuthorization()
        => CommandOutcomeAuthorizationOrderScenario.RunAsync();
}
