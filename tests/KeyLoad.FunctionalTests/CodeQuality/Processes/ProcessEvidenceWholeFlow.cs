using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.FunctionalTests.CodeQuality.Processes;

internal static class ProcessEvidenceWholeFlow
{
    internal const string SuccessMarker = "original-failures-projected-and-healthy-child-joined";
    private const string EvidenceRootEnvironmentKey = "KEYLOAD__EVIDENCE_ROOT";
    private const string EvidenceLimitsEnvironmentKey = "KEYLOAD__EVIDENCE_LIMITS";

    private const string Command = """
        $ErrorActionPreference = 'Stop'
        $root = $env:KEYLOAD__EVIDENCE_ROOT
        $limits = $env:KEYLOAD__EVIDENCE_LIMITS | ConvertFrom-Json -AsHashtable
        . (Join-Path $root 'scripts/Features/CodeQuality/functional-coverage.execution-merge.inputs.ps1')
        . (Join-Path $root 'scripts/Features/CodeQuality/functional-coverage.execution-merge.process.ps1')
        $script:FcMergeInput.ReadBufferBytes = [int] $limits.outputCharacters
        $script:FcMergeInput['SettlementTimeoutSeconds'] = [int] $limits.settlementSeconds
        function Verify-OriginalProjection($original) {
            $saved = @($original.failures)
            $types = @($saved | ForEach-Object { $_.GetType().FullName })
            $hResults = @($saved | ForEach-Object { $_.HResult })
            $texts = @($saved | ForEach-Object { $_.ToString() })
            $evidence = ConvertTo-FcCoverageProcessEvidence $original
            $json = ConvertTo-Json -InputObject $evidence -Depth 12 -Compress
            $parsed = ConvertFrom-Json -InputObject $json -AsHashtable -Depth 12
            Assert-FcExactKeys $parsed @('exitCode','stdout','stderr','failures','exitJoined','outputJoined','errorJoined','disposed')
            foreach ($name in @('exitCode','stdout','stderr','exitJoined','outputJoined','errorJoined','disposed')) {
                if ($parsed[$name] -cne $original[$name]) { throw 'An original execution process field changed.' }
            }
            if ($parsed.failures -isnot [Array] -or $parsed.failures.Count -ne $saved.Count -or $original.failures.Count -ne $saved.Count) {
                throw 'An original execution failure was omitted or replaced.'
            }
            for ($index = 0; $index -lt $saved.Count; $index++) {
                Assert-FcExactKeys $parsed.failures[$index] @('exceptionType','hResult','text')
                if (-not [object]::ReferenceEquals($saved[$index],$original.failures[$index]) -or
                    $parsed.failures[$index].exceptionType -cne $types[$index] -or
                    $parsed.failures[$index].hResult -ne $hResults[$index] -or
                    $parsed.failures[$index].text -cne $texts[$index]) {
                    throw 'An original execution exception identity or complete text changed.'
                }
            }
            if (-not $parsed.exitJoined -or -not $parsed.outputJoined -or -not $parsed.errorJoined -or -not $parsed.disposed) {
                throw 'An original execution owner did not settle.'
            }
        }
        $missingPath = Join-Path $root ('keyload-absent-child-' + [Guid]::NewGuid().ToString('N'))
        if ([IO.File]::Exists($missingPath) -or [IO.Directory]::Exists($missingPath)) { throw 'Missing-child input already exists.' }
        $missing = Invoke-FcCoverageProcess $missingPath @() $limits.timeoutSeconds $limits.outputCharacters
        if ($missing.exitCode -ne $script:FcMergeProcess.NotStartedExitCode -or
            $missing.failures.Count -eq 0 -or $missing.stdout -cne '' -or $missing.stderr -cne '') {
            throw 'The genuine missing executable did not retain its execution start failure.'
        }
        Verify-OriginalProjection $missing
        $failed = Invoke-FcCoverageProcess 'pwsh' @('-NoLogo','-NoProfile','-NonInteractive','-Command',
            '[Console]::Out.Write("original-output"); [Console]::Error.Write("original-error"); exit 17') $limits.timeoutSeconds $limits.outputCharacters
        if ($failed.exitCode -ne 17 -or $failed.failures.Count -ne 1 -or
            $failed.stdout -cne 'original-output' -or $failed.stderr -cne 'original-error' -or
            $failed.failures[0].GetType() -ne [InvalidOperationException] -or
            $failed.failures[0].Message -cne $script:FcMergeProcess.ExitMessage) {
            throw 'The genuine failed child lost original output or exit failure.'
        }
        Verify-OriginalProjection $failed
        $healthy = Invoke-FcCoverageProcess 'pwsh' @('-NoLogo','-NoProfile','-NonInteractive','-Command',
            '[Console]::Out.Write("healthy-output"); [Console]::Error.Write("healthy-error")') $limits.timeoutSeconds $limits.outputCharacters
        if ($healthy.exitCode -ne 0 -or $healthy.failures.Count -ne 0 -or
            $healthy.stdout -cne 'healthy-output' -or $healthy.stderr -cne 'healthy-error') {
            throw 'The same execution process owner failed its genuine healthy follow-up.'
        }
        Verify-OriginalProjection $healthy
        [Console]::Out.Write('original-failures-projected-and-healthy-child-joined')
        """;

    internal static Task<ProductionSourceManifestProcessResult> RunAsync(CancellationToken token)
    {
        var options = ProductionSourceManifestProcess.CaptureExecutionOptions();
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = ProductionSourceManifestProcess.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", Command })
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment[EvidenceRootEnvironmentKey] = ProductionSourceManifestProcess.RepositoryRoot;
        start.Environment[EvidenceLimitsEnvironmentKey] = JsonSerializer.Serialize(new
        {
            timeoutSeconds = checked((int)Math.Ceiling(options.Value.OrdinaryTimeout.TotalSeconds)),
            settlementSeconds = checked((int)Math.Ceiling(options.Value.ProcessSettlementTimeout.TotalSeconds)),
            outputCharacters = options.Value.CleanupOutputCharacters
        });
        return ProductionSourceManifestProcess.RunAsync(options, start, token);
    }
}
