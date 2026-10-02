using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class SiteBuilderProcess
{
    public static async Task<SiteProcessResult> RunAsync(SiteTestInputs inputs, string reports,
        string output, CancellationToken cancellationToken, string? additionalArgument = null)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(inputs, reports, output, additionalArgument),
            EnableRaisingEvents = true
        };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteTokens.BuilderDidNotStart);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SiteTokens.NodeTimeoutMilliseconds);
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
            return new(process.ExitCode, stdout, stderr);
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

    private static ProcessStartInfo CreateStartInfo(SiteTestInputs inputs, string reports, string output,
        string? additionalArgument)
    {
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = inputs.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(inputs.Repository, SiteAssetTokens.BuilderRelativePath));
        start.ArgumentList.Add($"{SiteTokens.ReportsArgument}={reports}");
        start.ArgumentList.Add($"{SiteTokens.OutputArgument}={output}");
        start.ArgumentList.Add($"{SiteTokens.RevisionArgument}={inputs.MeasuredRevision}");
        start.ArgumentList.Add($"{SiteTokens.EvidenceArgument}={inputs.EvidenceUrl}");
        if (additionalArgument is not null)
        {
            start.ArgumentList.Add(additionalArgument);
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
