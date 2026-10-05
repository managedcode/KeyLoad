namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CommandOutcomeAuthorizationOrderTests
{
    [Test]
    public Task DeniedOperationDoesNotDecodeCorruptLegacyOutcomeBeforeAuthorization()
        => CommandOutcomeAuthorizationOrderScenario.RunAsync();
}
