namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class AnalyticsCommandIdentityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task FreshAnalyticsIssuanceRetainsMixedReceiptAcrossMapOrderingConflictAndNativeCold(bool delete)
        => AnalyticsCommandIdentityFlow.RunAsync(delete, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task NonAnalyticsIssuanceRetainsPublicIdentityInFrozenFingerprint()
        => AnalyticsCommandIdentityAssertions.NonAnalyticsAsync();
}
