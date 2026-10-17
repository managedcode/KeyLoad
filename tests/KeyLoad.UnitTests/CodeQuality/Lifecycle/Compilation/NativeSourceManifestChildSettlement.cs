using System.Diagnostics;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

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
    internal const string StartFailure = "The native settlement child could not start.";
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
        => RunAsync(ProductionSourceManifestProcess.CaptureExecutionOptions(), TimeProvider.System, cancellationToken);

    internal static Task<NativeSourceManifestChildJoined> RunAsync(
        IOptions<TestExecutionOptions> executionOptions, CancellationToken cancellationToken)
        => RunAsync(executionOptions, TimeProvider.System, cancellationToken);

    internal static async Task<NativeSourceManifestChildJoined> RunAsync(
        IOptions<TestExecutionOptions> executionOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
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
                joined = await RunChildAsync(process, options, timeProvider, failures, cancellationToken).ConfigureAwait(false);
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
        return joined with
        {
            ProcessDisposed = disposed,
            OwnedRootDeleted = deleted
        };
    }

    private static async Task<NativeSourceManifestChildJoined?> RunChildAsync(Process process,
        TestExecutionOptions options, TimeProvider timeProvider, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var retained = new NativeSourceManifestChildOriginals();
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
                pending = await InterruptAfterReadinessAsync(retained, options, timeProvider, caller, failures,
                    cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            await retained.SettleAsync(process, options, timeProvider, failures).ConfigureAwait(false);
        }
        return await retained.JoinedAsync(process, pending, failures, caller.Token).ConfigureAwait(false);
    }

    private static async Task<bool> InterruptAfterReadinessAsync(NativeSourceManifestChildOriginals retained,
        TestExecutionOptions options, TimeProvider timeProvider, CancellationTokenSource caller, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        var ready = retained.Ready ?? throw new InvalidDataException(ReadinessFailure);
        var original = retained.Original ?? throw new InvalidOperationException(StartFailure);
        var marker = await ready.WaitAsync(options.OrdinaryTimeout, timeProvider,
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
