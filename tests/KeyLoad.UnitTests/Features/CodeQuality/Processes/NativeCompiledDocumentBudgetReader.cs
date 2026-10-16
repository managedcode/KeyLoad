using System.Diagnostics;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal static class NativeCompiledDocumentBudgetReader
{
    private const string ScriptName = "inspect-native-document-budgets.ps1";
    private const string Script = """
        param([string] $Repository, [string] $FixtureRoot)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        . (Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.shared.ps1')
        . (Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.compiled-identity.ps1')
        if ($script:FcCompiledIdentity.MaximumDocuments -ne 5000 -or
            $script:FcCompiledIdentity.MaximumGeneratedDocuments -ne 5000 -or
            $script:FcCompiledIdentity.MaximumArtifactBytes -ne 67108864) { throw 'Native document budget changed.' }
        $sources = @([ordered]@{ path = 'native-source.cs'; sha256 = Get-FcHash (Join-Path $FixtureRoot 'native-source.cs') })
        function Read-Original {
            Read-FcCompiledIdentity $FixtureRoot 'native-pathmap.dll' 'native-pathmap.pdb' $sources
        }
        function Require-BudgetRefusal([int] $SourceMaximum, [int] $GeneratedMaximum) {
            $script:FcCompiledIdentity.MaximumDocuments = $SourceMaximum
            $script:FcCompiledIdentity.MaximumGeneratedDocuments = $GeneratedMaximum
            try { $null = Read-Original }
            catch {
                if ($_.Exception.Message -cne $script:FcCompiledIdentity.InvalidDocument) { throw }
                return $true
            }
            throw 'The native document category budget was not enforced.'
        }
        try {
            $script:FcCompiledIdentity.MaximumDocuments = 1
            $script:FcCompiledIdentity.MaximumGeneratedDocuments = 1
            $first = Read-Original
            $sourceRefused = Require-BudgetRefusal 0 1
            $generatedRefused = Require-BudgetRefusal 1 0
            $script:FcCompiledIdentity.MaximumDocuments = 1
            $script:FcCompiledIdentity.MaximumGeneratedDocuments = 1
            $healthy = Read-Original
            [ordered]@{ first = $first; sourceRefused = $sourceRefused; generatedRefused = $generatedRefused; healthy = $healthy } |
                ConvertTo-Json -Depth 12 -Compress
        }
        catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        """;

    internal static async Task<ProductionSourceManifestProcessResult> ReadAsync(NativePathMapFixture fixture,
        CancellationToken token)
    {
        var script = Path.Combine(fixture.Root, ScriptName);
        await File.WriteAllTextAsync(script, Script, token);
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = fixture.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script,
            "-Repository", ProductionSourceManifestProcess.RepositoryRoot, "-FixtureRoot", fixture.Root })
        {
            start.ArgumentList.Add(argument);
        }
        return await ProductionSourceManifestProcess.RunAsync(fixture.ExecutionOptions, start, token);
    }
}
