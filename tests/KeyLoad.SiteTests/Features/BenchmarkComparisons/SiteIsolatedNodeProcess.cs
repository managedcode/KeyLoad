using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedNodeProcess
{
    private const int DeadlineSeconds = 300;
    private const string ProbeFailure = "The isolated real Node probe failed.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<JsonElement> RunAsync(SiteTestInputs inputs, object request,
        CancellationToken cancellationToken)
    {
        await using var temporary = SiteTempDirectory.Create();
        var script = Path.Combine(temporary.Path, "isolated-probe.mjs");
        var requestPath = Path.Combine(temporary.Path, "request.json");
        await File.WriteAllTextAsync(script, SiteIsolatedNodeProgram.Source, cancellationToken);
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        await File.WriteAllBytesAsync(requestPath, requestBytes, cancellationToken);
        using var requestDocument = JsonDocument.Parse(requestBytes);
        var admission = SiteHeavyChildClassification.IsProjection(requestDocument.RootElement)
            ? SiteHeavyChildAdmission.Shared : null;
        var start = CreateStart(inputs.Repository, script, requestPath);
        var result = await RunProcessAsync(start, cancellationToken, admission).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.StandardError.Length != 0)
        {
            throw new InvalidOperationException(ProbeFailure);
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        return document.RootElement.Clone();
    }

    public static async Task<SiteProcessResult> RunProcessAsync(ProcessStartInfo start, CancellationToken cancellationToken,
        SiteHeavyChildAdmission? admission = null)
    {
        using var lease = admission is null ? null : await admission.AcquireAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException(ProbeFailure);
        }
        lease?.MarkStarted(process);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(DeadlineSeconds));
        var stdout = SiteProcessOutput.ReadAsync(process.StandardOutput, ProbeFailure, deadline.Token);
        var stderr = SiteProcessOutput.ReadAsync(process.StandardError, ProbeFailure, deadline.Token);
        try
        {
            var exit = process.WaitForExitAsync(deadline.Token);
            var first = await Task.WhenAny(exit, stdout, stderr).ConfigureAwait(false);
            await first.ConfigureAwait(false);
            await exit.ConfigureAwait(false);
            var result = new SiteProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
            _ = lease?.CompleteIfSettled(stdout, stderr);
            return result;
        }
        catch (Exception)
        {
            await SiteHeavyChildLease.StopAndObserveAsync(process, stdout, stderr, lease);
            throw;
        }
    }

    private static ProcessStartInfo CreateStart(string repository, string script, string request)
    {
        var result = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        result.ArgumentList.Add(script);
        result.ArgumentList.Add(request);
        return result;
    }
}
