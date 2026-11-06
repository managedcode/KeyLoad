namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentSessionAuthorityColdTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public Task OriginalMinimumTokenRefusesDamagedColdAuthorityThenExactRepairReplayAndFreshWorkContinue(int damage)
        => DocumentSessionAuthorityColdTrial.RunAsync(damage);
}
