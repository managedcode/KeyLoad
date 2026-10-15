namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class RequiredProcessingPolicyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentRequiredBodyAndHeaderPolicyFencesProcessingBeforeAckAndEffectsAcrossRepairReplayAndTwoCold(bool header)
        => await RequiredProcessingPolicyFlow.RunAsync(header, TestContext.Current!.Execution.CancellationToken);
}
