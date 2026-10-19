namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class ConnectionFixtureConcurrencyTests
{
    [Test]
    public Task IndependentlyOwnedConnectionsReachBothOriginalBarriersAndSettle()
        => ConnectionScenario.RunAsync(left => ConnectionScenario.RunAsync(
            right => ConnectionFixtureConcurrencyFlow.RunAsync(left, right)));
}
