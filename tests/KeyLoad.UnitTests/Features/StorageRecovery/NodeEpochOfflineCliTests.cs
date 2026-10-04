using System.Diagnostics;
using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochOfflineCliTests
{
    private const string Dotnet = "dotnet";
    private const string ServerFile = "KeyLoad.Server.dll";
    private const string FailureCode = "Validation";
    private const int RunSeconds = 60;
    private const int CleanupSeconds = 10;

    [Test]
    [Arguments("upgrade-native-store")]
    [Arguments("prepare-native-node")]
    [Arguments("verify-native-node")]
    [Arguments("publish-native-node")]
    public async Task AcEpoch010InvalidOfflineArgumentsReturnOnlyCanonicalCodeWithoutStartingServer(string operation)
    {
        var root = IsolatedAggregateNodeProcess.RepositoryRoot();
        var server = Path.Combine(root, "src", "KeyLoad.Server", "bin", "Release", "net10.0", ServerFile);
        await Assert.That(File.Exists(server)).IsTrue();
        var start = new ProcessStartInfo(Dotnet)
        { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(server);
        start.ArgumentList.Add(operation);
        using var startOwner = IsolatedAggregateNodeStartOwner.Create(start);
        var process = startOwner.StartAndTransfer();
        var result = await IsolatedAggregateNodeLifetime.RunAsync(process,
            TimeSpan.FromSeconds(RunSeconds), TimeSpan.FromSeconds(CleanupSeconds),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).IsEqualTo(string.Empty);
        await Assert.That(result.Error).IsEqualTo(FailureCode + Environment.NewLine);
    }
}
