using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteVendorGzipOracle
{
    internal static async Task<string> RunAsync(string repository, string feature, CancellationToken token)
    {
        using var process = new Process { StartInfo = CreateStartInfo(repository, feature) };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteVendorTokens.OracleDidNotStart);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(SiteTokens.NodeTimeoutMilliseconds);
        var stdout = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteTokens.NodeOutputExceeded, timeout.Token);
        var stderr = SiteProcessOutput.ReadAsync(process.StandardError, SiteTokens.NodeOutputExceeded, timeout.Token);
        try
        {
            var exit = process.WaitForExitAsync(timeout.Token);
            await Task.WhenAny(exit, stdout, stderr);
            await exit;
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != SiteTokens.ProcessSuccessExitCode || error.Length != SiteTokens.Zero)
            {
                throw new InvalidOperationException(SiteVendorTokens.OracleFailure);
            }

            return output;
        }
        catch (Exception)
        {
            try
            {
                await SiteProcessCleanup.StopAsync(process);
            }
            finally
            {
                await SiteProcessCleanup.ObserveCapturesAsync(process, stdout, stderr);
            }

            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string repository, string feature)
    {
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(SiteVendorTokens.NodeTypeArgument);
        start.ArgumentList.Add(SiteVendorTokens.NodeEvalArgument);
        start.ArgumentList.Add(SiteVendorTokens.GzipOracleProgram);
        var root = Path.Combine(feature, SiteAssetTokens.ThreeVendorRelativePath);
        foreach (var name in SiteVendorTokens.VendorFiles)
        {
            start.ArgumentList.Add(Path.Combine(root, name));
        }

        return start;
    }
}
