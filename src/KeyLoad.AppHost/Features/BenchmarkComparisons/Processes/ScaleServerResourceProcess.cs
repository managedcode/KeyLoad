using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static partial class ScaleServerResourceProcess
{
    private const string Docker = "docker";
    private const string InspectFormat = "{{.Id}}|{{.Image}}|{{.State.Pid}}|{{.State.StartedAt}}|{{.State.Status}}|{{range .Mounts}}{{if .RW}}{{.Source}}~{{.Destination}}~{{.Type}};{{end}}{{end}}";
    private const string Separator = "|";

    internal static async Task<string?> InspectAsync(string container, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        var output = await RunAsync(Docker, ["inspect", "--format", InspectFormat, container], token, budget);
        if (output is null)
        {
            return null;
        }

        var fields = output.TrimEnd('\r', '\n').Split(Separator, StringSplitOptions.None);
        return fields.Length == 6 ? output.TrimEnd('\r', '\n') : null;
    }

    internal static async Task<string?> RunAsync(string executable, string[] arguments, CancellationToken token,
        ScaleServerResourceSampleBudget? budget = null, int maximumOutputBytes = ScaleServerResourceBounds.MaxHardwareBytes)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var limit = Math.Min(budget?.Remaining ?? ScaleServerResourceBounds.MaxHardwareBytes, maximumOutputBytes);
        if (limit < ScaleServerResourceBounds.MinimumCommandBytes)
        {
            throw new InvalidDataException("Server resource sample exceeded its bound.");
        }

        var errorLimit = Math.Min(ScaleServerResourceBounds.MaxFileBytes, Math.Max(1, limit / 8));
        var outputLimit = limit - errorLimit;
        process.Start();
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, outputLimit, token);
        var error = DrainBoundedAsync(process.StandardError.BaseStream, errorLimit, token);
        try
        {
            var exit = process.WaitForExitAsync(token);
            var readers = Task.WhenAll(output, error);
            if (await Task.WhenAny(exit, readers) == readers)
            {
                await readers;
            }

            await exit;
            await readers;
            var bytes = await output;
            budget?.Charge(bytes.Length + error.Result);
            return process.ExitCode == 0 ? Encoding.UTF8.GetString(bytes) : null;
        }
        catch (OperationCanceledException failure)
        {
            try
            { await TerminateAndJoinAsync(process, output, error); }
            catch (Exception cleanupFailure) when (SharesTerminalFailure(failure, cleanupFailure)) { }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Server resource process cancellation cleanup failed.", failure, cleanupFailure);
            }
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        catch (Exception failure)
        {
            try
            { await TerminateAndJoinAsync(process, output, error); }
            catch (Exception cleanupFailure) when (SharesTerminalFailure(failure, cleanupFailure)) { }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Server resource process failed and cleanup also failed.", failure, cleanupFailure);
            }
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private static bool SharesTerminalFailure(Exception failure, Exception cleanupFailure)
        => ReferenceEquals(failure, cleanupFailure)
            || failure is OperationCanceledException original && cleanupFailure is OperationCanceledException cleanup
                && original.CancellationToken == cleanup.CancellationToken;

    private static async Task TerminateAndJoinAsync(Process process, Task<byte[]> output, Task error)
    {
        if (!process.HasExited)
        {
            if (OperatingSystem.IsLinux())
            {
                _ = SendSignal(process.Id, 15);
            }

            var exited = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(1))) != exited)
            {
                try
                { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
            await process.WaitForExitAsync(CancellationToken.None);
        }
        await Task.WhenAll(output, error);
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        if (maximum < 1)
        {
            throw new InvalidDataException("Server resource sample exceeded its bound.");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(4096, maximum)];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, token);
            if (count == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + count >= maximum)
            {
                throw new InvalidDataException("Server resource sample exceeded its bound.");
            }

            buffer.Write(chunk, 0, count);
        }
    }

    private static async Task<int> DrainBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        if (maximum < 1)
        {
            throw new InvalidDataException("Server resource sample exceeded its bound.");
        }

        var buffer = new byte[maximum];
        var total = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length - total), token);
            if (count == 0)
            {
                return total;
            }

            total += count;
            if (total >= maximum)
            {
                throw new InvalidDataException("Server resource diagnostic exceeded its bound.");
            }
        }
    }
}
