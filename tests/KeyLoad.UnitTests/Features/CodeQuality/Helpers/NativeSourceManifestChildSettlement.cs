using System.Diagnostics;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal sealed record NativeSourceManifestChildJoined(Task Original, Task Exit, Task Output,
    Task Error, string Ready, string StandardOutput, string StandardError, bool PendingBeforeCancellation,
    bool ExitedBeforeDisposal, bool ProcessDisposed, bool OwnedRootDeleted,
    IReadOnlyList<Exception> Failures, CancellationToken Caller);

internal static class NativeSourceManifestChildSettlement
{
    internal const string ReadyMarker = "native-source-manifest-child-ready";
    internal const string ErrorMarker = "native-source-manifest-child-error";
    private const string DirectoryPrefix = "keyload-source-manifest-settlement-";
    private const string GuidFormat = "N";
    private const string PowerShellCommand = "pwsh";
    private const string NoLogoArgument = "-NoLogo";
    private const string NoProfileArgument = "-NoProfile";
    private const string NonInteractiveArgument = "-NonInteractive";
    private const string CommandArgument = "-Command";
    private const string StartFailure = "The native settlement child could not start.";
    private const string ReadinessFailure = "The native settlement child did not publish its readiness marker.";
    internal const string OutputFailure = "The native settlement child exceeded its captured output bound.";
    private const string ChildCommand = """
        [Console]::Error.WriteLine('native-source-manifest-child-error');
        [Console]::Error.Flush();
        [Console]::Out.WriteLine('native-source-manifest-child-ready');
        [Console]::Out.Flush();
        [Console]::In.ReadLine() | Out-Null;
        """;

    internal static Task<NativeSourceManifestChildJoined> RunAsync(CancellationToken cancellationToken)
        => RunAsync(ProductionSourceManifestProcess.CaptureExecutionOptions(), cancellationToken);

    internal static async Task<NativeSourceManifestChildJoined> RunAsync(
        IOptions<TestExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        cancellationToken.ThrowIfCancellationRequested();
        var options = executionOptions.Value;
        if (!options.IsValid())
        {
            throw new OptionsValidationException(TestExecutionOptions.SectionName, typeof(TestExecutionOptions),
                [TestExecutionOptions.ValidationMessage]);
        }
        var root = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        using var process = new Process { StartInfo = CreateStartInfo(root) };
        NativeSourceManifestChildJoined? joined = null;
        var disposed = false;
        var deleted = false;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                Directory.CreateDirectory(root);
                joined = await RunChildAsync(process, options, failures, cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            ServerFailureObserver.Observe(() =>
            {
                process.Dispose();
                disposed = true;
            }, failures);
            ServerFailureObserver.Observe(() =>
            {
                Directory.Delete(root, recursive: true);
                deleted = !Directory.Exists(root);
            }, failures);
        }
        if (joined is null)
        {
            NativeCoverageImageNodeSettlement.ThrowFailures(failures);
            throw new InvalidOperationException(StartFailure);
        }
        return joined with { ProcessDisposed = disposed, OwnedRootDeleted = deleted };
    }

    private static async Task<NativeSourceManifestChildJoined?> RunChildAsync(Process process,
        TestExecutionOptions options, List<Exception> failures, CancellationToken cancellationToken)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var retained = new NativeSourceManifestChildOriginals();
        Task? initialOriginal = null;
        var pending = false;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                retained.Started = process.Start();
                if (!retained.Started)
                {
                    throw new InvalidOperationException(StartFailure);
                }
                retained.Capture(process, options, failures);
                initialOriginal = retained.Original;
                pending = await InterruptAfterReadinessAsync(retained, options, caller, failures,
                    cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            await retained.SettleAsync(process, options, initialOriginal, failures).ConfigureAwait(false);
        }
        return await retained.JoinedAsync(process, pending, failures, caller.Token).ConfigureAwait(false);
    }

    private static async Task<bool> InterruptAfterReadinessAsync(NativeSourceManifestChildOriginals retained,
        TestExecutionOptions options, CancellationTokenSource caller, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        var ready = retained.Ready ?? throw new InvalidDataException(ReadinessFailure);
        var original = retained.Original ?? throw new InvalidOperationException(StartFailure);
        var marker = await ready.WaitAsync(options.OrdinaryTimeout, TimeProvider.System,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(marker, ReadyMarker, StringComparison.Ordinal))
        {
            throw new InvalidDataException(ReadinessFailure);
        }
        var pending = !original.IsCompleted;
        var interrupted = original.WaitAsync(caller.Token);
        await caller.CancelAsync().ConfigureAwait(false);
        await interrupted.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await ServerFailureObserver.ObserveAsync(() => interrupted, failures).ConfigureAwait(false);
        return pending;
    }

    internal static async Task<string> CaptureOutputAsync(Task<string?> ready, StreamReader reader,
        int maximumCharacters)
    {
        var marker = await ready.ConfigureAwait(false);
        var prefix = marker + Environment.NewLine;
        if (prefix.Length > maximumCharacters)
        {
            throw new InvalidDataException(OutputFailure);
        }
        var remainder = await NativeCoverageImageNodeOutput.ReadBoundedAsync(reader, maximumCharacters - prefix.Length)
            .ConfigureAwait(false);
        return prefix + remainder;
    }

    private static ProcessStartInfo CreateStartInfo(string root)
    {
        var start = new ProcessStartInfo(PowerShellCommand)
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(NoLogoArgument);
        start.ArgumentList.Add(NoProfileArgument);
        start.ArgumentList.Add(NonInteractiveArgument);
        start.ArgumentList.Add(CommandArgument);
        start.ArgumentList.Add(ChildCommand);
        return start;
    }
}

internal sealed class NativeSourceManifestChildOriginals
{
    private const string ExitConfirmationFailure = "The native settlement child did not confirm exit within its cleanup bound.";

    internal bool Started { get; set; }
    internal Task? Exit { get; private set; }
    internal Task<string?>? Ready { get; private set; }
    internal Task<string>? Output { get; private set; }
    internal Task<string>? Error { get; private set; }
    internal Task? Original { get; private set; }

    internal void Capture(Process process, TestExecutionOptions options, List<Exception> failures)
    {
        ServerFailureObserver.Observe(() => Exit ??= process.WaitForExitAsync(CancellationToken.None), failures);
        ServerFailureObserver.Observe(() => Ready ??= process.StandardOutput.ReadLineAsync(CancellationToken.None)
            .AsTask(), failures);
        ServerFailureObserver.Observe(() => Output ??= Ready is null
            ? NativeCoverageImageNodeOutput.ReadBoundedAsync(process.StandardOutput, options.CleanupOutputCharacters)
            : NativeSourceManifestChildSettlement.CaptureOutputAsync(Ready, process.StandardOutput,
                options.CleanupOutputCharacters), failures);
        ServerFailureObserver.Observe(() => Error ??= NativeCoverageImageNodeOutput.ReadBoundedAsync(
            process.StandardError, options.CleanupOutputCharacters), failures);
        Original = Task.WhenAll(ActualStages());
    }

    internal async Task SettleAsync(Process process, TestExecutionOptions options, Task? initialOriginal,
        List<Exception> failures)
    {
        if (!Started)
        {
            return;
        }
        Capture(process, options, failures);
        await ProductionSourceManifestProcessSettlement.JoinAsync(process, Original!,
            options.ProcessSettlementTimeout, failures).ConfigureAwait(false);
        await Original!.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (initialOriginal is not null)
        {
            await initialOriginal.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        await ConfirmNativeExitAsync(process, options, failures).ConfigureAwait(false);
    }

    internal static async Task ConfirmNativeExitAsync(Process process, TestExecutionOptions options,
        List<Exception> failures)
    {
        var started = TimeProvider.System.GetTimestamp();
        var missedDeadline = false;
        ServerFailureObserver.Observe(() => KillIfRunning(process), failures);
        while (true)
        {
            if (!missedDeadline && TimeProvider.System.GetElapsedTime(started) >= options.ProcessSettlementTimeout)
            {
                failures.Add(new TimeoutException(ExitConfirmationFailure));
                missedDeadline = true;
            }
            if (process.HasExited)
            {
                return;
            }
            await Task.Delay(options.ProcessExitPollInterval, TimeProvider.System, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    internal async Task<NativeSourceManifestChildJoined?> JoinedAsync(Process process, bool pending,
        List<Exception> failures, CancellationToken caller)
    {
        if (Original is null || Exit is null || Ready is null || Output is null || Error is null)
        {
            return null;
        }
        return new(Original, Exit, Output, Error, Ready.IsCompletedSuccessfully ? await Ready.ConfigureAwait(false) ?? string.Empty : string.Empty,
            Output.IsCompletedSuccessfully ? await Output.ConfigureAwait(false) : string.Empty,
            Error.IsCompletedSuccessfully ? await Error.ConfigureAwait(false) : string.Empty,
            pending, process.HasExited, false, false, failures, caller);
    }

    private IEnumerable<Task> ActualStages()
    {
        if (Exit is not null)
        {
            yield return Exit;
        }
        if (Output is not null)
        {
            yield return Output;
        }
        else if (Ready is not null)
        {
            yield return Ready;
        }
        if (Error is not null)
        {
            yield return Error;
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }
}
