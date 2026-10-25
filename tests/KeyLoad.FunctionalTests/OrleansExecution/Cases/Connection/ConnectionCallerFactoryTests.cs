namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionCallerFactoryTests
{
    [Test]
    public Task DuplicateRegistrationDoesNotStartLosingCallerAndBothOriginalAndFreshFactoriesSettle()
        => ConnectionCallerFactoryFlow.RunAsync();
}
