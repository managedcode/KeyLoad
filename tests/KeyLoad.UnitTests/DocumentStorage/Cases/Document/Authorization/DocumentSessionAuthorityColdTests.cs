namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentSessionAuthorityColdTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public Task OriginalMinimumTokenRefusesDamagedColdAuthorityThenExactRepairReplayAndFreshWorkContinue(int damage)
        => DocumentSessionAuthorityColdTrial.RunAsync(damage);
}
