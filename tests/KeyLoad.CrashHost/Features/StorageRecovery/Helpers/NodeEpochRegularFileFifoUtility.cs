using System.Diagnostics;
using System.Text;

namespace KeyLoad.CrashHost;

internal static class NodeEpochRegularFileFifoUtility
{
    private const string FailedStart = "Could not create test FIFO.";
    private const string FailedResult = "The bounded test FIFO utility failed.";
    private const string CleanupFailureKey = "KeyLoad.NodeEpochFifoCleanupFailure";

    internal static async Task CreateAsync(string path)
    {
        const int EmptyExitCode = 0;
        const int EmptyStandardOutputLength = 0;
        const int EmptyStandardErrorLength = 0;

        var executionOptions = CrashExecutionOptions.Child();
        var settings = executionOptions.Value;
        var start = CreateStart(path);
        using var process = Process.Start(start) ?? throw new InvalidOperationException(FailedStart);
        using var timeout = new CancellationTokenSource(settings.FifoExecutionTimeout);
        var output = ReadBoundedAsync(process.StandardOutput, executionOptions, timeout.Token);
        var error = ReadBoundedAsync(process.StandardError, executionOptions, timeout.Token);
        Exception? primaryFailure = null;
        try
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var standardOutput = await output;
            var standardError = await error;
            if (process.ExitCode != EmptyExitCode || standardOutput.Length != EmptyStandardOutputLength || standardError.Length != EmptyStandardErrorLength)
            { throw new InvalidOperationException(FailedResult); }
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally { await SettleAsync(process, output, error, primaryFailure, executionOptions); }
    }

    private static ProcessStartInfo CreateStart(string path)
    {
        const string CreateStartFileNameText = "mkfifo";

        var start = new ProcessStartInfo(CreateStartFileNameText)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(path);
        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, Microsoft.Extensions.Options.IOptions<CrashHostExecutionOptions> executionOptions, CancellationToken token)
    {
        const int EmptyCount = 0;
        const int StartIndexEmptyCount = 0;

        var settings = executionOptions.Value;
        var output = new StringBuilder();
        var chunk = new char[settings.FifoReadChunkCharacters];
        while (true)
        {
            var count = await reader.ReadAsync(chunk.AsMemory(), token);
            if (count == EmptyCount)
            { return output.ToString(); }
            if (count > settings.FifoMaximumOutputCharacters - output.Length)
            { throw new InvalidDataException(FailedResult); }
            output.Append(chunk, StartIndexEmptyCount, count);
        }
    }

    private static async Task SettleAsync(Process process, Task<string> output, Task<string> error,
        Exception? primaryFailure, Microsoft.Extensions.Options.IOptions<CrashHostExecutionOptions> executionOptions)
    {
        var settings = executionOptions.Value;
        try
        {
            using var cleanup = new CancellationTokenSource(settings.FifoCleanupTimeout);
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
