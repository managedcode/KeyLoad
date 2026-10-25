using System.Diagnostics;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.FunctionalTests.CodeQuality.Processes;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.CodeQuality.Helpers;

internal static class FunctionalReportAdmissionFlow
{
    private const string PowerShell = "pwsh";
    private const string ReaderName = "execution-report-admission-control.ps1";
    private const string FixtureName = "report-admission.fixture.json";
    private const string ExpectedName = "report-admission.expected.json";
    private const string Completion = "execution-report-admission-controls-complete";
    private const int SuccessfulExitCode = 0;
    private const long MinimumFixtureBytes = 1;
    private const string FixtureDirectory = "tests/KeyLoad.FunctionalTests/CodeQuality/TestData";
    private const string Reader = """
        param([string] $Repository, [string] $EvidenceRoot, [int] $ReadBufferBytes,
            [int] $MaximumFiles, [long] $MaximumTotalBytes, [long] $MaximumReportBytes,
            [int] $MaximumPathCharacters)
        $ErrorActionPreference = 'Stop'
        Set-StrictMode -Version Latest
        $root = Join-Path $Repository 'scripts/Features/CodeQuality'
        foreach ($file in @('compiled-identity','execution-merge.inputs','execution-merge.contributors',
            'execution-merge.unit-inventory','execution-merge.trx','execution-merge.functional-report','execution-merge.files')) {
            . (Join-Path $root ('functional-coverage.' + $file + '.ps1'))
        }
        $script:FcMergeInput.ReadBufferBytes = $ReadBufferBytes
        $script:FcMergeInput.MaximumFiles = $MaximumFiles
        $script:FcMergeInput.MaximumTotalBytes = $MaximumTotalBytes
        $script:FcMergeInput.MaximumFileBytes = $MaximumReportBytes
        $script:FcMergeInput.ReferenceCatalog = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        $fixturePath = Join-Path $EvidenceRoot 'report-admission.fixture.json'
        $expectedPath = Join-Path $EvidenceRoot 'report-admission.expected.json'
        function Read-Original([string] $Name, [string] $Path) {
            $reference = [ordered]@{ path=$Name; length=(Get-Item -LiteralPath $Path).Length;
                sha256=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
            Read-FcJsonReference $EvidenceRoot $reference $MaximumReportBytes $MaximumPathCharacters
        }
        $fixture = Read-Original 'report-admission.fixture.json' $fixturePath
        $expected = Read-Original 'report-admission.expected.json' $expectedPath
        $report = $fixture.value
        $cases = @($expected.value.cases)
        $originalRepository = [string] $expected.value.originalRepository
        $observed = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        $baseline = @(Read-FcFunctionalCases $report $cases 'unit' $originalRepository -MatchSourceLocations -ObservedIds $observed)
        foreach ($mutation in @('duplicate','swapped','source','status','unknown-field')) {
            $changed = [Text.Encoding]::UTF8.GetString($fixture.file.bytes) | ConvertFrom-Json -AsHashtable -DateKind String
            switch ($mutation) {
                'duplicate' { $changed.groups[0].tests[0].id = $changed.groups[0].tests[1].id }
                'swapped' { $first=$changed.groups[0].tests[0].id; $changed.groups[0].tests[0].id=$changed.groups[0].tests[1].id; $changed.groups[0].tests[1].id=$first }
                'source' { $changed.groups[0].tests[0].lineNumber += 1 }
                'status' { $changed.groups[0].tests[0].status = 'failed' }
                'unknown-field' { $changed.groups[0].tests[0].unexpected = $true }
            }
            $accepted = $false
            $partial = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
            try {
                $null=Read-FcFunctionalCases $changed $cases 'unit' $originalRepository -MatchSourceLocations -RequiredIds $observed -ObservedIds $partial
                $accepted=$true
            }
            catch { if ($_.Exception.Message -cne $script:FcFunctionalReport.Invalid) { throw } }
            if ($accepted -or $partial.Count -ne 0) { throw 'Changed report admitted or partial identity map exported.' }
            $restored = Read-Original 'report-admission.fixture.json' $fixturePath
            $healthy=@(Read-FcFunctionalCases $restored.value $cases 'unit' $originalRepository -MatchSourceLocations -RequiredIds $observed)
            if (($healthy -join "`n") -cne ($baseline -join "`n") -or $restored.file.sha256 -cne $fixture.file.sha256) {
                throw 'Original report restoration changed.'
            }
        }
        Write-Output 'execution-report-admission-controls-complete'
        """;

    internal static async Task VerifyAsync(string evidenceRoot, IOptions<TestExecutionOptions> executionOptions,
        CancellationToken token)
    {
        var limits = new CoverageExecutionOptions();
        await Assert.That(limits.IsValid()).IsTrue();
        var fixtureRoot = Path.Combine(ProductionSourceManifestProcess.RepositoryRoot,
            FixtureDirectory);
        foreach (var name in new[] { FixtureName, ExpectedName })
        {
            var source = Path.Combine(fixtureRoot, name);
            await Assert.That(new FileInfo(source).Length >= MinimumFixtureBytes && new FileInfo(source).Length <= limits.MaximumReportBytes).IsTrue();
            File.Copy(source, Path.Combine(evidenceRoot, name), overwrite: false);
        }
        var reader = Path.Combine(evidenceRoot, ReaderName);
        await File.WriteAllTextAsync(reader, Reader, token);
        var start = new ProcessStartInfo(PowerShell)
        {
            WorkingDirectory = evidenceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", reader,
            "-Repository", ProductionSourceManifestProcess.RepositoryRoot, "-EvidenceRoot", evidenceRoot,
            "-ReadBufferBytes", limits.ReadBufferBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-MaximumFiles", limits.MaximumFiles.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-MaximumTotalBytes", limits.MaximumTotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-MaximumReportBytes", limits.MaximumReportBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-MaximumPathCharacters", limits.MaximumPathCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }
        var result = await ProductionSourceManifestProcess.RunAsync(executionOptions, start, token);
        await Assert.That(result.ExitCode).IsEqualTo(SuccessfulExitCode).Because(result.StandardError);
        await Assert.That(result.StandardOutput.Trim()).IsEqualTo(Completion);
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
    }
}
