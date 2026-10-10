namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class NativeTextCapturedRf3Tests
{
    [Test]
    [Arguments(NativeTextMaintenancePath.Sdk, false)]
    [Arguments(NativeTextMaintenancePath.Mcp, false)]
    [Arguments(NativeTextMaintenancePath.SdkSql, false)]
    [Arguments(NativeTextMaintenancePath.McpSql, false)]
    [Arguments(NativeTextMaintenancePath.Sdk, true)]
    [Arguments(NativeTextMaintenancePath.Mcp, true)]
    [Arguments(NativeTextMaintenancePath.SdkSql, true)]
    [Arguments(NativeTextMaintenancePath.McpSql, true)]
    public Task GenuinePublicOriginalPostingReaderOverlapsCompleteNativeSwapThenJoinsBeforeFullHealthyReplayAndCold(
        NativeTextMaintenancePath path, bool cancelOriginal)
        => NativeTextCapturedRf3Scenario.RunAsync(path, cancelOriginal, TestContext.Current!.Execution.CancellationToken);
}
