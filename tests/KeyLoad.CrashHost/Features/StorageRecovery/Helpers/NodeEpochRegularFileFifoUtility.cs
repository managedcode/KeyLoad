using System.Diagnostics;
using System.Text;

namespace KeyLoad.CrashHost;

internal static class NodeEpochRegularFileFifoUtility
{
    private const string FailedStart = "Could not create test FIFO.";
    private const string FailedResult = "The bounded test FIFO utility failed.";
    private const string CleanupFailureKey = "KeyLoad.NodeEpochFifoCleanupFailure";
    private const int TimeoutSeconds = 5;
    private const int MaximumOutputCharacters = 4096;
    private const int ChunkCharacters = 256;

    internal static async Task CreateAsync(string path)
    {
        var start = CreateStart(path);
        using var process = Process.Start(start) ?? throw new InvalidOperationException(FailedStart);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
        var error = ReadBoundedAsync(process.StandardError, timeout.Token);
        Exception? primaryFailure = null;
        try
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var standardOutput = await output;
            var standardError = await error;
            if (process.ExitCode != 0 || standardOutput.Length != 0 || standardError.Length != 0)
            { throw new InvalidOperationException(FailedResult); }
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally { await SettleAsync(process, output, error, primaryFailure); }
    }

    private static ProcessStartInfo CreateStart(string path)
    {
        var start = new ProcessStartInfo("mkfifo")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(path);
        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var output = new StringBuilder();
        var chunk = new char[ChunkCharacters];
        while (true)
        {
            var count = await reader.ReadAsync(chunk.AsMemory(), token);
            if (count == 0)
            { return output.ToString(); }
            if (count > MaximumOutputCharacters - output.Length)
            { throw new InvalidDataException(FailedResult); }
            output.Append(chunk, 0, count);
        }
    }

    private static async Task SettleAsync(Process process, Task<string> output, Task<string> error,
        Exception? primaryFailure)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cleanup.Token);
            }
            await Task.WhenAll(output, error).WaitAsync(cleanup.Token);
        }
        catch (Exception cleanupFailure)
        {
            if (primaryFailure is null)
            { throw; }
            primaryFailure.Data[CleanupFailureKey] = cleanupFailure;
        }
    }
}
