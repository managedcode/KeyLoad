using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Captures a bounded real comparison-host startup result.</summary>
/// <param name="ExitCode">Child-process exit code.</param>
/// <param name="Stdout">Bounded drained standard output.</param>
/// <param name="Stderr">Bounded drained standard error.</param>
internal sealed record ComparisonHostExit(int ExitCode, string Stdout, string Stderr);

/// <summary>Runs the Release comparison host with isolated benchmark configuration.</summary>
internal static class ComparisonHostProcess
{
    private const string StartupFailureMessage = "The comparison host did not start.";
    private const int StartupTimeoutSeconds = 20;
    private const int CleanupTimeoutSeconds = 5;

    internal static async Task<ComparisonHostExit> RunAsync(string[] arguments,
        IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var startInfo = ComparisonHostLaunchSettings.Create(arguments, environment);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(StartupFailureMessage);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var captureLifetime = new CancellationTokenSource();
        var stdout = new ComparisonHostOutputCapture();
        var stderr = new ComparisonHostOutputCapture();
        var stdoutTask = stdout.CaptureAsync(process.StandardOutput, captureLifetime.Token);
        var stderrTask = stderr.CaptureAsync(process.StandardError, captureLifetime.Token);
        var captures = Task.WhenAll(stdoutTask, stderrTask);
        deadline.CancelAfter(TimeSpan.FromSeconds(StartupTimeoutSeconds));
        var stage = ComparisonHostStartupStage.Unavailable;
        try
        {
            stage = ComparisonHostStartupStage.ProcessExit;
            await process.WaitForExitAsync(deadline.Token);
            stage = ComparisonHostStartupStage.OutputDrain;
            var text = await captures.WaitAsync(deadline.Token);
            return new(process.ExitCode, text[0], text[1]);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var diagnostics = ComparisonHostStartupDiagnostics.Create(stage, process,
                stdout.Snapshot(), stderr.Snapshot());
            throw ComparisonHostStartupDiagnostics.CreateTimeoutException(diagnostics);
        }
        finally
        {
            await CleanupAsync(process, captureLifetime, captures);
        }
    }

    private static async Task ObserveCaptureAsync(Task capture)
    {
        try
        {
            await capture.WaitAsync(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            // Cancellation released an active pipe read.
        }
        catch (ObjectDisposedException)
        {
            // Closing the stream released an active pipe read.
        }
        catch (IOException)
        {
            // Process termination closed an active pipe read.
        }
        catch (TimeoutException)
        {
            _ = capture.ContinueWith(static completed => _ = completed.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static async Task ObserveExitAsync(Process process)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
        {
            // Exit observation reached its cleanup bound.
        }
    }

    private static async Task CleanupAsync(Process process, CancellationTokenSource captureLifetime,
        Task captures)
    {
        try
        {
            KillIfRunning(process);
            await ObserveExitAsync(process);
        }
        finally
        {
            await StopCapturesAsync(process, captureLifetime, captures);
        }
    }

    private static async Task StopCapturesAsync(Process process, CancellationTokenSource captureLifetime, Task captures)
    {
        var cancellation = captureLifetime.CancelAsync();
        try
        {
            CloseReader(process.StandardOutput);
            CloseReader(process.StandardError);
        }
        finally
        {
            try
            {
                await ObserveCaptureAsync(cancellation);
            }
            finally
            {
                await ObserveCaptureAsync(captures);
            }
        }
    }

    private static void CloseReader(StreamReader reader)
    {
        try
        {
            reader.Dispose();
        }
        catch (IOException)
        {
            // A terminated pipe can fail while closing.
        }
        catch (ObjectDisposedException)
        {
            // Cancellation can close a reader concurrently.
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
        catch (InvalidOperationException)
        {
            // The child exited concurrently with cleanup.
        }
    }
}

/// <summary>Constructs the real host launch without inherited benchmark secrets.</summary>
internal static class ComparisonHostLaunchSettings
{
    private const string DotnetCommand = "dotnet";
    private const string SolutionName = "KeyLoad.slnx";
    private const string BenchmarksDirectory = "benchmarks";
    private const string HostDirectory = "KeyLoad.ComparisonHost";
    private const string HostProjectName = "KeyLoad.ComparisonHost.csproj";
    private const string HostAssemblyName = "KeyLoad.ComparisonHost.dll";
    private const string BuildOutputDirectory = "bin";
    private const string ReleaseConfiguration = "Release";
    private const string TargetFramework = "net10.0";
    private const string BenchmarkEnvironmentPrefix = "Benchmarks__";
    private const string ConnectionEnvironmentPrefix = "ConnectionStrings__";
    private const string BenchmarkColonEnvironmentPrefix = "Benchmarks:";
    private const string ConnectionColonEnvironmentPrefix = "ConnectionStrings:";
    private const string MissingRepositoryMessage = "The KeyLoad repository root is unavailable.";
    private const string MissingHostMessage = "The Release comparison host is not built.";

    internal static ProcessStartInfo Create(string[] arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var root = RepositoryRoot();
        var assembly = Path.Combine(root, BenchmarksDirectory, HostDirectory, BuildOutputDirectory,
            ReleaseConfiguration, TargetFramework, HostAssemblyName);
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException(MissingHostMessage, assembly);
        }

        var startInfo = new ProcessStartInfo(DotnetCommand)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var key in startInfo.Environment.Keys.Where(IsBenchmarkSetting).ToArray())
        {
            startInfo.Environment.Remove(key);
        }
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }
        return startInfo;
    }

    private static bool IsBenchmarkSetting(string key)
        => key.StartsWith(BenchmarkEnvironmentPrefix, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(ConnectionEnvironmentPrefix, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(BenchmarkColonEnvironmentPrefix, StringComparison.OrdinalIgnoreCase)
            || key.StartsWith(ConnectionColonEnvironmentPrefix, StringComparison.OrdinalIgnoreCase);

    private static string RepositoryRoot()
    {
        foreach (var path in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionName))
                    && File.Exists(Path.Combine(directory.FullName, BenchmarksDirectory, HostDirectory, HostProjectName)))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(MissingRepositoryMessage);
    }
}
