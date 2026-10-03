using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class SiteBuilderProcess
{
    private const string IsolatedEnvironment = "KEYLOAD_SITE_ISOLATED_AGGREGATE";
    private const string IsolatedArgument = "--isolated=";
    private const int IsolatedTimeoutMilliseconds = 300_000;

    public static async Task<SiteProcessResult> RunAsync(SiteTestInputs inputs, string reports,
        string output, CancellationToken cancellationToken, string? additionalArgument = null,
        bool committedSource = true)
    {
        var arguments = CreateArguments(inputs, reports, output, additionalArgument, committedSource);
        var builderSources = await SiteBuilderDiagnostics.ReadBuilderSourcesAsync(inputs, cancellationToken);
        using var process = new Process
        {
            StartInfo = CreateStartInfo(inputs, arguments),
            EnableRaisingEvents = true
        };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteTokens.BuilderDidNotStart);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Environment.GetEnvironmentVariable(IsolatedEnvironment) is null
            ? SiteTokens.NodeTimeoutMilliseconds : IsolatedTimeoutMilliseconds);
        var stdoutTask = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteTokens.BuilderOutputExceeded, timeout.Token);
        var stderrTask = SiteProcessOutput.ReadAsync(process.StandardError, SiteTokens.BuilderOutputExceeded, timeout.Token);
        try
        {
            var exitTask = process.WaitForExitAsync(timeout.Token);
            var firstCompleted = await Task.WhenAny(exitTask, stdoutTask, stderrTask);
            await firstCompleted;
            await exitTask;
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var result = new SiteProcessResult(process.ExitCode, stdout, stderr);
            await SiteBuilderDiagnostics.RetainAsync(inputs, arguments, builderSources, result, cancellationToken);
            return result;
        }
        catch (Exception)
        {
            try
            {
                await SiteProcessCleanup.StopAsync(process);
            }
            finally
            {
                await SiteProcessCleanup.ObserveCapturesAsync(process, stdoutTask, stderrTask);
            }

            throw;
        }
    }

    private static string[] CreateArguments(SiteTestInputs inputs, string reports, string output,
        string? additionalArgument, bool committedSource)
    {
        var arguments = new List<string>
        {
            Path.Combine(inputs.Repository, SiteAssetTokens.BuilderRelativePath),
            $"{SiteTokens.ReportsArgument}={reports}",
            $"{SiteTokens.OutputArgument}={output}",
            $"{SiteTokens.RevisionArgument}={inputs.MeasuredRevision}",
            $"{SiteTokens.EvidenceArgument}={inputs.EvidenceUrl}",
        };
        if (committedSource)
        {
            arguments.Add($"{SiteTokens.SiteRevisionArgument}={inputs.SiteRevision}");
        }

        if (committedSource && Environment.GetEnvironmentVariable(IsolatedEnvironment) is { } isolated)
        {
            arguments.Add(IsolatedArgument + isolated);
        }

        if (additionalArgument is not null)
        {
            arguments.Add(additionalArgument);
        }

        return arguments.ToArray();
    }

    private static ProcessStartInfo CreateStartInfo(SiteTestInputs inputs, string[] arguments)
    {
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = inputs.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }
}

internal sealed class SiteTempDirectory(string path) : IAsyncDisposable
{
    public string Path { get; } = path;
    public string Output => System.IO.Path.Combine(Path, SiteAssetTokens.OutputDirectory);
    public string Reports => System.IO.Path.Combine(Path, SiteAssetTokens.ReportsDirectory);

    public static SiteTempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{SiteAssetTokens.TempDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new(path);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
