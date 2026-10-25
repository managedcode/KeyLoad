using System.Diagnostics;
using KeyLoad.FunctionalTests.CodeQuality.Helpers;

namespace KeyLoad.FunctionalTests.CodeQuality.Processes;

internal static class PathMapIdentityReader
{
    private const string PowerShell = "pwsh";
    private const string ReaderFileName = "inspect-execution-pathmap.ps1";
    private const string ReaderScript = """
        param([string] $Repository, [string] $FixtureRoot)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        try {
            . (Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.shared.ps1')
            . (Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.compiled-identity.ps1')
            $sources = @([ordered]@{
                path = 'execution-source.cs'
                sha256 = Get-FcHash (Join-Path $FixtureRoot 'execution-source.cs')
            })
            $identity = Read-FcCompiledIdentity $FixtureRoot 'execution-pathmap.dll' 'execution-pathmap.pdb' $sources
            ConvertTo-Json -InputObject $identity -Depth 10 -Compress
        }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        """;

    internal static async Task<ProductionSourceManifestProcessResult> ReadAsync(PathMapFixture fixture,
        CancellationToken cancellationToken)
    {
        var script = Path.Combine(fixture.Root, ReaderFileName);
        await File.WriteAllTextAsync(script, ReaderScript, cancellationToken);
        var start = new ProcessStartInfo(PowerShell)
        {
            WorkingDirectory = fixture.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-Repository");
        start.ArgumentList.Add(ProductionSourceManifestProcess.RepositoryRoot);
        start.ArgumentList.Add("-FixtureRoot");
        start.ArgumentList.Add(fixture.Root);
        return await ProductionSourceManifestProcess.RunAsync(fixture.ExecutionOptions, start, cancellationToken);
    }
}
