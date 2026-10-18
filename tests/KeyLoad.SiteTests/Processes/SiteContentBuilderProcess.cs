using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteContentBuilderProcess
{
    public static Task<SiteCoverageProcessResult> BuildAsync(SiteContentInputs inputs, string output,
        CancellationToken token, params string[] extraArguments) => RunAsync(inputs,
        ["--benchmarks=none", "--output=" + output, "--site-revision=" + inputs.SiteRevision, .. extraArguments], token);

    public static Task<SiteCoverageProcessResult> RunAsync(SiteContentInputs inputs, IReadOnlyList<string> arguments,
        CancellationToken token)
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
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return SiteCoverageNodeProcess.RunAsync(start, token);
    }
}
