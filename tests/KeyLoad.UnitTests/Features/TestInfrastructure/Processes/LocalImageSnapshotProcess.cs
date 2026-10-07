using System.Diagnostics;
using System.Text;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed record LocalImageSnapshotProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class LocalImageSnapshotProcess
{
    private const string NodeCommand = "node";
    private const string StartFailure = "The local image snapshot Node process did not start.";
    private const string OutputFailure = "The local image snapshot Node process exceeded its output bound.";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(60);
    private const int MaximumStreamBytes = 8 * 1024;
    private const int ReadBufferCharacters = 1024;

    internal static IOptions<TestExecutionOptions> CaptureExecutionOptions()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        return AppHostOptionsRegistration.BindTestExecution(configuration);
    }

    internal static async Task<LocalImageSnapshotProcessResult> RunAsync(string repositoryRoot, string modulePath,
        string program, string ownedRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(modulePath);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(ownedRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var options = CaptureExecutionOptions().Value;
        var timeProvider = TimeProvider.System;
        using var process = new Process { StartInfo = CreateStartInfo(repositoryRoot, modulePath, program, ownedRoot) };
        var failures = new List<Exception>();
        var started = false;
        LocalImageSnapshotProcessResult? result = null;
        try
        {
            ServerFailureObserver.Observe(() => started = process.Start(), failures);
            if (!started && failures.Count == 0)
            { failures.Add(new InvalidOperationException(StartFailure)); }
            if (started)
            {
                result = await RunStartedAsync(process, options, timeProvider, failures, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        finally
        {
            try
            { process.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(StartFailure);
    }

    private static async Task<LocalImageSnapshotProcessResult?> RunStartedAsync(Process process,
        TestExecutionOptions options, TimeProvider timeProvider, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        var exit = process.WaitForExitAsync(CancellationToken.None);
        var output = ReadBoundedAsync(process.StandardOutput);
        var error = ReadBoundedAsync(process.StandardError);
        var outputWatch = StopOnReaderFailureAsync(output, process);
        var errorWatch = StopOnReaderFailureAsync(error, process);
        var original = Task.WhenAll(exit, outputWatch, errorWatch);
        await ObserveOperationAsync(original, failures, timeProvider, cancellationToken).ConfigureAwait(false);
        await NativeCoverageImageNodeSettlement.SettleAsync(process, true, original, null, null, null,
            options.ProcessSettlementTimeout, timeProvider, options.TerminationGrace,
            options.ProcessExitPollInterval, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => exit, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => outputWatch, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => errorWatch, failures).ConfigureAwait(false);
        if (failures.Count != 0 || !original.IsCompletedSuccessfully)
        { return null; }
        return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    private static async Task ObserveOperationAsync(Task original, List<Exception> failures,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(OperationTimeout, timeProvider);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await original.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            failures.Add(cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(cancellationToken)
                : new TimeoutException("The local image snapshot operation exceeded its deadline."));
        }
        catch (Exception error) when (original.IsFaulted && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            // The original process and reader tasks are observed after bounded settlement.
        }
        catch (Exception error) when (original.IsFaulted && !NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            // The original process and reader tasks are observed after bounded settlement.
        }
    }

    private static async Task<string> StopOnReaderFailureAsync(Task<string> reader, Process process)
    {
        try
        { return await reader.ConfigureAwait(false); }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => StopOwnedProcess(process), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static void StopOwnedProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            { process.Kill(entireProcessTree: true); }
        }
        catch (InvalidOperationException) when (process.HasExited) { }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var builder = new StringBuilder(MaximumStreamBytes);
        var buffer = new char[ReadBufferCharacters];
        var bytes = 0;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0)
            { return builder.ToString(); }
            bytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, count));
            if (bytes > MaximumStreamBytes)
            { throw new InvalidDataException(OutputFailure); }
            builder.Append(buffer, 0, count);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string repositoryRoot, string modulePath, string program,
        string ownedRoot)
    {
        var start = new ProcessStartInfo(NodeCommand)
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--input-type=module");
        start.ArgumentList.Add("--eval");
        start.ArgumentList.Add(program);
        start.ArgumentList.Add(modulePath);
        start.ArgumentList.Add(ownedRoot);
        return start;
    }
}
