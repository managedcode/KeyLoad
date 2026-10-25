using System.Diagnostics;
using KeyLoad.FunctionalTests.CodeQuality.Helpers;

namespace KeyLoad.FunctionalTests.CodeQuality.Processes;

internal static class CompiledDocumentBudgetReader
{
    private const string ScriptName = "inspect-execution-document-budgets.ps1";
    private const string Script = """
        param([string] $Repository, [string] $FixtureRoot)
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        . (Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.shared.ps1')
        . (Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.compiled-identity.ps1')
        if ($script:FcCompiledIdentity.MaximumDocuments -ne 5000 -or
            $script:FcCompiledIdentity.MaximumGeneratedDocuments -ne 5000 -or
            $script:FcCompiledIdentity.MaximumArtifactBytes -ne 67108864) { throw 'Execution document budget changed.' }
        $sources = @([ordered]@{ path = 'execution-source.cs'; sha256 = Get-FcHash (Join-Path $FixtureRoot 'execution-source.cs') })
        function Read-Original {
            Read-FcCompiledIdentity $FixtureRoot 'execution-pathmap.dll' 'execution-pathmap.pdb' $sources
        }
        function Require-BudgetRefusal([int] $SourceMaximum, [int] $GeneratedMaximum) {
            $script:FcCompiledIdentity.MaximumDocuments = $SourceMaximum
            $script:FcCompiledIdentity.MaximumGeneratedDocuments = $GeneratedMaximum
            try { $null = Read-Original }
            catch {
                if ($_.Exception.Message -cne $script:FcCompiledIdentity.InvalidDocument) { throw }
                return $true
            }
            throw 'The execution document category budget was not enforced.'
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

    internal static async Task<ProductionSourceManifestProcessResult> ReadAsync(PathMapFixture fixture,
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
