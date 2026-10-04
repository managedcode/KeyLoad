using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteQualificationSource
{
    public static async Task RequireCheckoutAsync(string repository, string revision,
        CancellationToken cancellationToken)
    {
        if (!SiteCoverageSourceManifestWriter.IsRevision(revision))
        {
            throw new InvalidOperationException(SitePublicationTokens.SourceFailure);
        }

        var start = new ProcessStartInfo(SitePublicationTokens.GitExecutable)
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(SitePublicationTokens.GitDirectoryArgument);
        start.ArgumentList.Add(repository);
        start.ArgumentList.Add(SitePublicationTokens.GitRevisionArgument);
        start.ArgumentList.Add(SitePublicationTokens.GitHeadArgument);
        var result = await SiteCoverageNodeProcess.RunAsync(start, cancellationToken);
        if (result.ExitCode != SiteTokens.ProcessSuccessExitCode ||
            result.StandardError.Trim().Length != SiteTokens.Zero || result.StandardOutput.Trim() != revision)
        {
            throw new InvalidOperationException(SitePublicationTokens.SourceFailure);
        }
    }
}
