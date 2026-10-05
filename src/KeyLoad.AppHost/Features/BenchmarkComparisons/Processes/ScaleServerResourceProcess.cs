using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static partial class ScaleServerResourceProcess
{
    private const string ScaleServerResourceProcessMetadataName = "libc";
    private const string ScaleServerResourceProcessScaleServerResourceProcessMetadataName = "kill";

    private const string Docker = "docker";
    private const string InspectFormat = "{{.Id}}|{{.Image}}|{{.State.Pid}}|{{.State.StartedAt}}|{{.State.Status}}|{{range .Mounts}}{{if .RW}}{{.Source}}~{{.Destination}}~{{.Type}};{{end}}{{end}}";
    private const string Separator = "|";

    internal static async Task<string?> InspectAsync(string container, ScaleServerResourceSampleBudget budget, CancellationToken token)
    {
        const string ArgumentsText = "inspect";
        const string InspectAsyncArgumentsText = "--format";
        const char CarriageReturnCharacter = '\r';
        const char LineFeedCharacter = '\n';
        const int InspectFieldCount = 6;

        var output = await RunAsync(Docker, [ArgumentsText, InspectAsyncArgumentsText, InspectFormat, container], token, budget);
        if (output is null)
        {
            return null;
        }

        var fields = output.TrimEnd(CarriageReturnCharacter, LineFeedCharacter).Split(Separator, StringSplitOptions.None);
        return fields.Length == InspectFieldCount ? output.TrimEnd(CarriageReturnCharacter, LineFeedCharacter) : null;
    }

    internal static async Task<string?> RunAsync(string executable, string[] arguments, CancellationToken token,
        ScaleServerResourceSampleBudget budget, int? maximumOutputBytes = null)
    {
        const string MessageText = "Server resource sample exceeded its bound.";
        const int InspectFieldCount = 0;
        const string RunAsyncMessageText = "Server resource process cancellation cleanup failed.";

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

        var limit = Math.Min(budget.Remaining, maximumOutputBytes ?? budget.Settings.MaxHardwareBytes);
        if (limit < budget.Settings.MinimumCommandBytes)
        {
            throw new InvalidDataException(MessageText);
        }

        var errorLimit = Math.Min(budget.Settings.MaxFileBytes, Math.Max(1, limit / 8));
        var outputLimit = limit - errorLimit;
        process.Start();
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, outputLimit, budget.Settings.NativeReadBufferBytes, token);
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
            return process.ExitCode == InspectFieldCount ? Encoding.UTF8.GetString(bytes) : null;
        }
        catch (OperationCanceledException failure)
        {
            try
            { await TerminateAndJoinAsync(process, output, error, budget.Settings.ProcessSettlement); }
            catch (Exception cleanupFailure) when (SharesTerminalFailure(failure, cleanupFailure)) { }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(RunAsyncMessageText, failure, cleanupFailure);
            }
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        catch (Exception failure)
        {
            try
            { await TerminateAndJoinAsync(process, output, error, budget.Settings.ProcessSettlement); }
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

    private static async Task TerminateAndJoinAsync(Process process, Task<byte[]> output, Task error, TimeSpan settlement)
    {
        const int SignalValue = 15;

        if (!process.HasExited)
        {
            if (OperatingSystem.IsLinux())
            {
                _ = SendSignal(process.Id, SignalValue);
            }

            var exited = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(exited, Task.Delay(settlement)) != exited)
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

    [LibraryImport(ScaleServerResourceProcessMetadataName, EntryPoint = ScaleServerResourceProcessScaleServerResourceProcessMetadataName, SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, int chunkBytes, CancellationToken token)
    {
        const int BoundaryValue = 1;
        const string MessageText = "Server resource sample exceeded its bound.";
        const int InspectFieldCount = 0;
        const int OffsetValue = 0;

        if (maximum < BoundaryValue)
        {
            throw new InvalidDataException(MessageText);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(chunkBytes, maximum)];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, token);
            if (count == InspectFieldCount)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + count >= maximum)
            {
                throw new InvalidDataException(MessageText);
            }

            buffer.Write(chunk, OffsetValue, count);
        }
    }

    private static async Task<int> DrainBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        const int BoundaryValue = 1;
        const string MessageText = "Server resource sample exceeded its bound.";
        const int TotalInitialValue = 0;
        const int StartValue = 0;
        const int InspectFieldCount = 0;
        const string DrainBoundedAsyncMessageText = "Server resource diagnostic exceeded its bound.";

        if (maximum < BoundaryValue)
        {
            throw new InvalidDataException(MessageText);
        }

        var buffer = new byte[maximum];
        var total = TotalInitialValue;
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(StartValue, buffer.Length - total), token);
            if (count == InspectFieldCount)
            {
                return total;
            }

            total += count;
            if (total >= maximum)
            {
                throw new InvalidDataException(DrainBoundedAsyncMessageText);
            }
        }
    }
}
