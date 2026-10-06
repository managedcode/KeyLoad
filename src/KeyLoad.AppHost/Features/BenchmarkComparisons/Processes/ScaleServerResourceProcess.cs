using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static partial class ScaleServerResourceProcess
{
    private const int RunAsyncFirstPositiveCount = 1;
    private const string ProcessAndCleanupFailureMessage = "Server resource process failed and cleanup also failed.";

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

        var output = await RunAsync(Docker, [ArgumentsText, InspectAsyncArgumentsText, InspectFormat, container], budget, token);
        if (output is null)
        {
            return null;
        }

        var fields = output.TrimEnd(CarriageReturnCharacter, LineFeedCharacter).Split(Separator, StringSplitOptions.None);
        return fields.Length == InspectFieldCount ? output.TrimEnd(CarriageReturnCharacter, LineFeedCharacter) : null;
    }

    internal static async Task<string?> RunAsync(string executable, string[] arguments,
        ScaleServerResourceSampleBudget budget, CancellationToken cancellationToken, int? maximumOutputBytes = null)
    {
        const string MessageText = "Server resource sample exceeded its bound.";
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

        var limit = Math.Min(budget.Remaining, maximumOutputBytes ?? budget.Settings.MaxNativeOutputBytes);
        if (limit < budget.Settings.MinimumCommandBytes)
        {
            throw new InvalidDataException(MessageText);
        }

        var errorLimit = Math.Min(budget.Settings.MaxFileBytes, Math.Max(RunAsyncFirstPositiveCount, limit / budget.Settings.StandardErrorOutputDivisor));
        var outputLimit = limit - errorLimit;
        process.Start();
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, outputLimit, budget.Settings.NativeReadBufferBytes, cancellationToken);
        var error = DrainBoundedAsync(process.StandardError.BaseStream, errorLimit, cancellationToken);
        var exit = process.WaitForExitAsync(cancellationToken);
        var readers = Task.WhenAll(output, error);
        return await ObserveAsync(process, output, error, exit, readers, budget);
    }

    private static async Task<string?> ObserveAsync(Process process, Task<byte[]> output, Task<int> error,
        Task exit, Task readers, ScaleServerResourceSampleBudget budget)
    {
        const int SuccessfulExitCode = 0;
        const string CancellationCleanupFailureMessage = "Server resource process cancellation cleanup failed.";
        try
        {
            if (await Task.WhenAny(exit, readers) == readers)
            {
                await readers;
            }
            await exit;
            await readers;
            var bytes = await output;
            var diagnosticBytes = await error;
            budget.Charge(bytes.Length + diagnosticBytes);
            return process.ExitCode == SuccessfulExitCode ? Encoding.UTF8.GetString(bytes) : null;
        }
        catch (Exception failure)
        {
            try
            {
                await ScaleServerResourceProcessSettlement.SettleAsync(process, output, error, exit, readers,
                    budget.Settings.ProcessSettlement, SendSignal, failure);
            }
            catch (Exception cleanupFailure)
            {
                var message = failure is OperationCanceledException ? CancellationCleanupFailureMessage : ProcessAndCleanupFailureMessage;
                throw new AggregateException(message, failure, cleanupFailure);
            }
            throw;
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport(ScaleServerResourceProcessMetadataName, EntryPoint = ScaleServerResourceProcessScaleServerResourceProcessMetadataName, SetLastError = true)]
    private static partial int SendSignal(int processId, int signal);

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, int chunkBytes, CancellationToken token)
    {
        const int BoundaryValue = 1;
        const string MessageText = "Server resource sample exceeded its bound.";
        const int EndOfStreamReadCount = 0;
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
            if (count == EndOfStreamReadCount)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + count >= maximum)
            {
                throw new InvalidDataException(MessageText);
            }

            await buffer.WriteAsync(chunk.AsMemory(OffsetValue, count), token);
        }
    }

    private static async Task<int> DrainBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        const int BoundaryValue = 1;
        const string MessageText = "Server resource sample exceeded its bound.";
        const int TotalInitialValue = 0;
        const int StartValue = 0;
        const int EndOfStreamReadCount = 0;
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
            if (count == EndOfStreamReadCount)
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
