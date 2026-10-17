using System.Diagnostics;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal static class NativePathMapIdentityReader
{
    private const string PowerShell = "pwsh";
    private const string ReaderFileName = "inspect-native-pathmap.ps1";
    private const string ReaderScript = """
        param([string] $Repository, [string] $FixtureRoot)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        try {
            . (Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.shared.ps1')
            . (Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.compiled-identity.ps1')
            $sources = @([ordered]@{
                path = 'native-source.cs'
                sha256 = Get-FcHash (Join-Path $FixtureRoot 'native-source.cs')
            })
            $identity = Read-FcCompiledIdentity $FixtureRoot 'native-pathmap.dll' 'native-pathmap.pdb' $sources
            ConvertTo-Json -InputObject $identity -Depth 10 -Compress
        }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        """;

    internal static async Task<ProductionSourceManifestProcessResult> ReadAsync(NativePathMapFixture fixture,
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
