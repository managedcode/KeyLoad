using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record ProbeRequest(string Operation, string? ReportPath = null, string? Scenario = null,
    string? Metric = null, string? Repetition = null, double[]? Values = null, JsonElement? Value = null,
    string? ExpectedRevision = null, string? FilePath = null, JsonElement? Entry = null, string? BaseUrl = null);

internal sealed record ProbeResult(JsonElement Value, bool Accepted = true)
{
    public JsonElement Property(string name) => Value.GetProperty(name);
}

internal static class SiteNodeProbe
{
    public static Task<ProbeResult> RunAsync(SiteTestInputs inputs, ProbeRequest request, CancellationToken cancellationToken)
        => RunRawAsync(inputs, JsonSerializer.Serialize(request, SiteTokens.JsonOptions), cancellationToken);

    public static async Task<ProbeResult> RunRawAsync(SiteTestInputs inputs, string input, CancellationToken cancellationToken)
    {
        using var process = StartProcess(inputs);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SiteTokens.NodeTimeoutMilliseconds);
        var result = await CaptureProcess(process, input, timeout.Token);
        return ParseResponse(result, process.ExitCode);
    }

    private static Process StartProcess(SiteTestInputs inputs)
    {
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = inputs.Repository,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(inputs.Repository, SiteAssetTokens.ProbeRelativePath));
        start.Environment[SiteTokens.NodeRepositoryEnvironment] = inputs.Repository;
        start.Environment[SiteTokens.ReportsEnvironment] = inputs.Reports;
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (process.Start())
            {
                return process;
            }

            throw new InvalidOperationException(SiteTokens.NodeProbeDidNotStart);
        }
        catch (Exception)
        {
            process.Dispose();
            throw;
        }
    }

    private static async Task<(string StandardOutput, string StandardError)> CaptureProcess(
        Process process, string input, CancellationToken cancellationToken)
    {
        var stdoutTask = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteTokens.NodeOutputExceeded, cancellationToken);
        var stderrTask = SiteProcessOutput.ReadAsync(process.StandardError, SiteTokens.NodeOutputExceeded, cancellationToken);
        try
        {
            await process.StandardInput.WriteLineAsync(input.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var firstCompleted = await Task.WhenAny(exitTask, stdoutTask, stderrTask);
            await firstCompleted;
            await exitTask;
            return (await stdoutTask, await stderrTask);
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

    private static ProbeResult ParseResponse((string StandardOutput, string StandardError) result, int exitCode)
    {
        if (exitCode != SiteTokens.ProcessSuccessExitCode || result.StandardError.Length != SiteTokens.Zero)
        {
            throw new InvalidOperationException($"{SiteTokens.NodeProbeFailure} {exitCode}; {SiteTokens.StandardErrorLabel} {result.StandardError}");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var response = document.RootElement;
        if (!response.GetProperty(SiteTokens.Ok).GetBoolean())
        {
            return new(response.Clone(), Accepted: false);
        }
        return new(response.GetProperty(SiteTokens.Result).Clone());
    }
}
