using System.Diagnostics;
using System.Text;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Launches and owns the real embedded BenchmarkDotNet executable.</summary>
internal static class EmbeddedBenchmarkProcess
{
    private const string DotnetCommand = "dotnet";
    private const string SolutionFileName = "KeyLoad.slnx";
    private const string BenchmarksDirectoryName = "benchmarks";
    private const string ExecutableProjectDirectoryName = "KeyLoad.Benchmarks";
    private const string BuildOutputDirectoryName = "bin";
    private const string ReleaseConfigurationName = "Release";
    private const string TargetFrameworkName = "net10.0";
    private const string ExecutableAssemblyName = "KeyLoad.Benchmarks.dll";
    private const string JobOption = "--job";
    private const string DryJobName = "Dry";
    private const string ExporterOption = "--exporters";
    private const string FullJsonExporterName = "fulljson";
    private const string FilterOption = "--filter";
    private const string AllBenchmarksFilter = "*";
    private const string ArtifactsOption = "--artifacts";
    private const string WorkingDirectoryMissingMessage = "The KeyLoad solution root is unavailable.";
    private const string ExecutableMissingMessage = "The Release embedded benchmark executable is not built.";
    private const string ProcessStartFailureMessage = "The embedded benchmark executable did not start.";
    private const string ProcessTimeoutMessage = "The generated embedded benchmark consumer exceeded its deadline.";
    private const int ReadBufferLength = 4_096;
    private const int MaximumCapturedCharacters = 65_536;
    private const int ProcessTimeoutMinutes = 10;
    private const int CleanupTimeoutSeconds = 15;

    internal const string GuidFormat = "N";
    internal const int SuccessExitCode = 0;

    internal static Task<EmbeddedBenchmarkProcessResult> RunAsync(string artifactDirectory,
        CancellationToken cancellationToken)
        => RunAsync(artifactDirectory, TimeSpan.FromMinutes(ProcessTimeoutMinutes), cancellationToken);

    internal static async Task<EmbeddedBenchmarkProcessResult> RunAsync(string artifactDirectory, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        using var process = new Process { StartInfo = CreateStartInfo(artifactDirectory) };
        if (!process.Start())
        {
            throw new InvalidOperationException(ProcessStartFailureMessage);
        }

        using var captureCancellation = new CancellationTokenSource();
        var standardOutput = CaptureAsync(process.StandardOutput, captureCancellation.Token);
        var standardError = CaptureAsync(process.StandardError, captureCancellation.Token);
        var captures = Task.WhenAll(standardOutput, standardError);
        try
        {
            using var deadline = new CancellationTokenSource(timeout);
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            try
            {
                await process.WaitForExitAsync(runCancellation.Token);
                var output = await captures.WaitAsync(runCancellation.Token);
                return new(process.ExitCode, output[0], output[1]);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(ProcessTimeoutMessage);
            }
        }
        finally
        {
            await StopAsync(process, captureCancellation, captures);
        }
    }

    internal static string RepositoryRoot()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(WorkingDirectoryMissingMessage);
    }

    private static ProcessStartInfo CreateStartInfo(string artifactDirectory)
    {
        var root = RepositoryRoot();
        var executablePath = Path.Combine(root, BenchmarksDirectoryName, ExecutableProjectDirectoryName,
            BuildOutputDirectoryName, ReleaseConfigurationName, TargetFrameworkName, ExecutableAssemblyName);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(ExecutableMissingMessage, executablePath);
        }

        var startInfo = new ProcessStartInfo(DotnetCommand)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add(JobOption);
        startInfo.ArgumentList.Add(DryJobName);
        startInfo.ArgumentList.Add(ExporterOption);
        startInfo.ArgumentList.Add(FullJsonExporterName);
        startInfo.ArgumentList.Add(FilterOption);
        startInfo.ArgumentList.Add(AllBenchmarksFilter);
        startInfo.ArgumentList.Add(ArtifactsOption);
        startInfo.ArgumentList.Add(artifactDirectory);
        return startInfo;
    }

    private static async Task<string> CaptureAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var captured = new StringBuilder();
        var buffer = new char[ReadBufferLength];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                var remaining = MaximumCapturedCharacters - captured.Length;
                if (remaining > 0)
                {
                    captured.Append(buffer, 0, Math.Min(count, remaining));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return captured.ToString();
        }

        return captured.ToString();
    }

    private static async Task StopAsync(Process process, CancellationTokenSource captureCancellation, Task captures)
    {
        try
        {
            await TerminateAsync(process);
        }
        finally
        {
            await captureCancellation.CancelAsync();
            await captures.WaitAsync(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
        }
    }

    private static async Task TerminateAsync(Process process)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                return;
            }
        }

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
        await process.WaitForExitAsync(cleanup.Token);
    }
}

/// <summary>Contains the real generated consumer's process result.</summary>
internal sealed record EmbeddedBenchmarkProcessResult(int ExitCode, string StandardOutput, string StandardError);
