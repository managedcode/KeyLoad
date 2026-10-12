namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class LargeStreamCleanupPermissionTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ActualLinuxPermissionFaultPreservesInitiatingEvidenceAndSurfacesCleanupOnlyFailure(bool retain)
    {
        if (!OperatingSystem.IsLinux())
        { throw new PlatformNotSupportedException("This actual permission-fault control requires Linux."); }
        await LargeStreamCleanupPermissionTrial.RunAsync(retain,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
    }
}
