using System.Diagnostics;
using System.Text.Json;
using KeyLoad.CrashHost;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class NodeEpochRegularFileProcess
{
    private const string Dotnet = "dotnet";
    private const string Mkfifo = "mkfifo";
    private const int TimeoutSeconds = 15;
    private const int CleanupSeconds = 10;
    private const string ExpectedOutput = "FormatUnsupported";
    private const string FailedStart = "The bounded regular-file probe did not start.";

    internal static async Task AssertRejectedAsync(string source, string destination, EpochPriorNodeProfile profile,
        string fifo, CancellationToken cancellationToken)
    {
        var observed = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(fifo)!)
            .Any(path => string.Equals(path, fifo, StringComparison.Ordinal));
        await Assert.That(observed).IsTrue();
        var result = await RunProbeAsync(source, destination, profile, cancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        await Assert.That(result.Output.Trim()).IsEqualTo(ExpectedOutput);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsFalse();
    }

    internal static async Task CreateFifoAsync(string path, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Mkfifo)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(path);
        var result = await RunBoundedAsync(start, null, cancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
    }

    private static Task<EpochPriorProcessResult> RunProbeAsync(string source, string destination,
        EpochPriorNodeProfile profile, CancellationToken cancellationToken)
    {
        return RunCrashHostAsync(source, destination, profile, NodeEpochRegularFileScenario.Mode,
            cancellationToken);
    }

    internal static Task<EpochPriorProcessResult> RunReplacementProbeAsync(string source, string retained,
        EpochPriorNodeProfile profile, CancellationToken cancellationToken)
        => RunCrashHostAsync(source, retained, profile, NodeEpochRegularFileScenario.ReplacementMode,
            cancellationToken);

    internal static async Task AssertImageRejectedAsync(string source, string output,
        EpochPriorNodeProfile profile, CancellationToken cancellationToken)
    {
        var result = await RunCrashHostAsync(source, output, profile, NodeEpochRegularFileScenario.ImageMode,
            cancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        await Assert.That(result.Output.Trim()).IsEqualTo(ExpectedOutput);
        await Assert.That(File.Exists(output)).IsFalse();
    }

    private static Task<EpochPriorProcessResult> RunCrashHostAsync(string source, string destination,
        EpochPriorNodeProfile profile, string mode, CancellationToken cancellationToken)
    {
        var start = CreateCrashHostStart(source, destination, mode);
        var request = JsonSerializer.Serialize(profile, EpochPriorSourceProbe.JsonOptions);
        return RunBoundedAsync(start, request, cancellationToken);
    }

    private static ProcessStartInfo CreateCrashHostStart(string source, string destination, string mode)
    {
        var start = new ProcessStartInfo(Dotnet)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(CrashHostMarker).Assembly.Location);
        start.ArgumentList.Add(source);
        start.ArgumentList.Add(destination);
        start.ArgumentList.Add(mode);
        return start;
    }

    private static async Task<EpochPriorProcessResult> RunBoundedAsync(ProcessStartInfo start, string? input,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException(FailedStart);
        using var timeoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        var output = EpochPriorProcessOutput.ReadAsync(process.StandardOutput, timeout.Token);
        var error = EpochPriorProcessOutput.ReadAsync(process.StandardError, timeout.Token);
        Exception? activeFailure = null;
        try
        {
            await WriteInputAsync(process, input, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode, await output, await error);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds), TimeProvider.System);
            await EpochPriorProcessOutput.SettleAsync(process, output, error, activeFailure, cleanup.Token);
        }
    }

    private static async Task WriteInputAsync(Process process, string? input, CancellationToken cancellationToken)
    {
        if (input is not null)
        { await process.StandardInput.WriteLineAsync(input.AsMemory(), cancellationToken); }
        process.StandardInput.Close();
    }
}
