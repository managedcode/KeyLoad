using System.Diagnostics;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal static class NativeUnitImageAdmissionReader
{
    private const string PowerShell = "pwsh";
    private const string ReaderFileName = "read-native-unit-image.ps1";
    private const string ImageFileName = "functional-coverage.test-image.unit.json";
    private const string ReaderScript = """
        param([string] $Repository, [string] $ImagePath)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        try {
            foreach ($name in @('shared','compiled-identity','compile-identity','inventory','test-identity')) {
                . (Join-Path $Repository ('scripts/Features/CodeQuality/functional-coverage.' + $name + '.ps1'))
            }
            Read-FcTestIdentityManifest $ImagePath $Repository | Out-Null
        }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        """;

    internal static async Task<ProductionSourceManifestProcessResult> ReadAsync(string evidenceRoot,
        IOptions<TestExecutionOptions> executionOptions, CancellationToken token)
    {
        var script = Path.Combine(evidenceRoot, ReaderFileName);
        await File.WriteAllTextAsync(script, ReaderScript, token);
        var start = new ProcessStartInfo(PowerShell)
        {
            WorkingDirectory = evidenceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-Repository",
            ProductionSourceManifestProcess.RepositoryRoot, "-ImagePath", Path.Combine(evidenceRoot, ImageFileName)
        })
        {
            start.ArgumentList.Add(argument);
        }
        return await ProductionSourceManifestProcess.RunAsync(executionOptions, start, token);
    }
}
