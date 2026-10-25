using System.Diagnostics;
using KeyLoad.FunctionalTests.CodeQuality.Assertions;
using KeyLoad.FunctionalTests.CodeQuality.Processes;
using KeyLoad.ProcessRecoveryTests.AnalyticsStore;

namespace KeyLoad.FunctionalTests.CodeQuality.Cases;

internal sealed class RecoveryProcessEntryPointTests
{
    [Test]
    public async Task ReferencedRecoveryExecutableStartsFromUnitTestOutputWithCompleteRuntimeDependencies()
    {
        var directory = Path.GetDirectoryName(typeof(RecoveryProcessEntryPointTests).Assembly.Location)!;
        var assembly = Path.Combine(directory, "KeyLoad.ProcessRecoveryTests.dll");
        await Assert.That(File.Exists(assembly)).IsTrue();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--list-tests");
        start.ArgumentList.Add("--treenode-filter");
        start.ArgumentList.Add("/*/*/UserAnalyticsDerivedProcessRecoveryTests/*");
        var result = await ProductionSourceManifestProcess.RunAsync(
            ProductionSourceManifestProcess.CaptureExecutionOptions(), start,
            TestContext.Current!.Execution.CancellationToken);
        await PathMapAssertions.SuccessfulProcessAsync(result);
        await Assert.That(result.StandardOutput).Contains(nameof(UserAnalyticsDerivedProcessRecoveryTests
            .KilledRealGenerationReopensSamePhysicalRootPreservingCanonicalReceiptAndHealthyAnalytics));
    }
}
