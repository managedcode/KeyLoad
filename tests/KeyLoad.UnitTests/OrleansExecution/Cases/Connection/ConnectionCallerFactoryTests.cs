namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class ConnectionCallerFactoryTests
{
    [Test]
    public Task DuplicateRegistrationDoesNotStartLosingCallerAndBothOriginalAndFreshFactoriesSettle()
        => ConnectionCallerFactoryFlow.RunAsync();
}
