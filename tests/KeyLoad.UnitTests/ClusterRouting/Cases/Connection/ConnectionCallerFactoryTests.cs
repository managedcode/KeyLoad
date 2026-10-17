namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionCallerFactoryTests
{
    [Test]
    public Task DuplicateRegistrationDoesNotStartLosingNativeCallerAndBothOriginalAndFreshFactoriesSettle()
        => ConnectionCallerFactoryFlow.RunAsync();
}
