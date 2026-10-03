using System.Diagnostics;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochPriorExecutableProcess
{
    private const string Executable = "dotnet";
    private const string FailedStart = "The actual prior-executable probe did not start.";
    private const int TimeoutSeconds = 25;
    private const int CleanupSeconds = 15;

    internal static async Task<EpochPriorProcessResult> RunAsync(string executable, string request,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(executable);
        start.ArgumentList.Add(EpochPriorSourceProbe.Mode);
        using var process = Process.Start(start) ?? throw new InvalidOperationException(FailedStart);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        var output = EpochPriorProcessOutput.ReadAsync(process.StandardOutput, timeout.Token);
        var error = EpochPriorProcessOutput.ReadAsync(process.StandardError, timeout.Token);
        Exception? activeFailure = null;
        try
        {
            await process.StandardInput.WriteLineAsync(request.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            var exit = process.WaitForExitAsync(timeout.Token);
            var completed = await Task.WhenAny(exit, output, error);
            if (completed.IsFaulted)
            {
                await completed;
            }
            await exit;
            return new(process.ExitCode, await output, await error);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
            await EpochPriorProcessOutput.SettleAsync(process, output, error, activeFailure, cleanup.Token);
        }
    }
}

internal sealed record EpochPriorProcessResult(int ExitCode, string Output, string Error);

internal static class EpochPriorProcessOutput
{
    private const int MaximumCharacters = 65536;
    private const int ChunkCharacters = 1024;
    private const string OutputExceeded = "The prior-executable probe output exceeds its bounded protocol.";
    private const string CleanupFailureKey = "KeyLoad.EpochPriorProbeCleanupFailure";

    internal static async Task<string> ReadAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var output = new System.Text.StringBuilder();
        var chunk = new char[ChunkCharacters];
        while (true)
        {
            var count = await reader.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return output.ToString();
            }
            if (count > MaximumCharacters - output.Length)
            {
                throw new InvalidDataException(OutputExceeded);
            }
            output.Append(chunk, 0, count);
        }
    }

    internal static async Task SettleAsync(Process process, Task<string> output, Task<string> error,
        Exception? activeFailure, CancellationToken cancellationToken)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            await Task.WhenAll(output, error).WaitAsync(cancellationToken);
        }
        catch (Exception cleanupFailure)
        {
            if (activeFailure is null)
            {
                throw;
            }
            activeFailure.Data[CleanupFailureKey] = cleanupFailure;
        }
    }
}
