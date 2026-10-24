using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.TestInfrastructure;

/// <summary>Owns bounded execution metadata subprocesses and joins them after every outcome.</summary>
internal static class ProjectProcessObservationCommand
{
    private const int SuccessfulExit = 0;
    private const int StreamCompleted = 0;
    private const int BufferStart = 0;
    private const int ReadBufferCharacters = 256;
    private const string OutputExceeded = "Execution project launch metadata exceeds its observation bound.";

    internal static async Task<string> ReadAsync(ProcessStartInfo start, IOptions<TestExecutionOptions> options,
        TimeSpan executionTimeout, CancellationToken token)
    {
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var timeout = new CancellationTokenSource(executionTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        Exception? primary = null;
        var output = ReadBoundedAsync(process.StandardOutput, options.Value.CleanupOutputCharacters, deadline.Token);
        var error = ReadBoundedAsync(process.StandardError, options.Value.CleanupOutputCharacters, deadline.Token);
        try
        {
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            if (process.ExitCode != SuccessfulExit)
            {
                throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
            }
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            primary = failure;
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            primary = failure;
        }
        await JoinAsync(process, primary, options).ConfigureAwait(false);
        return await output.ConfigureAwait(false);
    }

    private static async Task JoinAsync(Process process, Exception? primary, IOptions<TestExecutionOptions> options)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            using var cleanup = new CancellationTokenSource(options.Value.ApplicationCleanupTimeout, TimeProvider.System);
            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception failure) when (primary is not null && CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            throw new AggregateException(primary, failure);
        }
        catch (Exception failure) when (primary is not null && CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            throw new AggregateException(primary, failure);
        }
        if (primary is not null)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[ReadBufferCharacters];
        int read;
        while ((read = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) != StreamCompleted)
        {
            if (read > maximumCharacters - text.Length)
            {
                throw new InvalidDataException(OutputExceeded);
            }
            text.Append(buffer, BufferStart, read);
        }
        return text.ToString();
    }
}
