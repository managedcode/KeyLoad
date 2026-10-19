param([Parameter(Mandatory)][string] $Repository, [Parameter(Mandatory)][string] $EvidenceRoot,
    [Parameter(Mandatory)][string] $SourceManifestPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'functional-coverage.shared.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.inputs.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.contributors.ps1')
$Repository = [IO.Path]::GetFullPath($Repository)
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$relativeEvidence = [IO.Path]::GetRelativePath($Repository, $EvidenceRoot).Replace('\', '/')
if ((Resolve-FcPath $Repository $relativeEvidence) -cne $EvidenceRoot) { throw 'Evidence root must be confined and existing.' }
$inventory = Get-Content (Join-Path $PSScriptRoot $script:FcContributors.UnitInventoryName) -Raw | ConvertFrom-Json -AsHashtable
$rows = [Collections.Generic.List[object]]::new()
$common = @('scripts/Features/TestInfrastructure/run-tests.mjs','--KeyLoadTests:ReportTrx=true')
$coverage = @('--KeyLoadTests:CoverageSettings=scripts/Features/CodeQuality/functional-coverage.production.settings.xml',
    '--KeyLoadTests:CoverageFormat=coverage')

function Invoke-WorkflowUnit([string] $Id, [string] $Suite, [string] $Filter, [bool] $Covered) {
    $directory = Join-Path $EvidenceRoot $Id
    $arguments = $common + @(('--KeyLoadTests:Suite=' + $Suite),('--KeyLoadTests:ResultsDirectory=' + $directory))
    if ($Filter -cne '') { $arguments += '--KeyLoadTests:Filter=' + $Filter }
    if ($Covered) { $arguments += $coverage + @('--KeyLoadTests:CoverageOutput=' + (Join-Path $directory 'coverage.coverage')) }
    & node @arguments
    $code = [int] $LASTEXITCODE
    $rows.Add([ordered]@{ id = $Id; suite = $Suite; filter = $Filter; coverageEnabled = $Covered
        exitCode = $code; resultsDirectory = $Id })
}

if ([Environment]::GetEnvironmentVariable('DOTNET_EnableHWIntrinsic') -ceq '0') {
    throw 'Disabled hardware intrinsics are not an admitted current qualification profile.'
}
foreach ($suite in @('unit')) {
    Invoke-WorkflowUnit ($suite + '-census') $suite $script:FcContributors.FullUnitSelector $false
    foreach ($group in $inventory.coverageGroups) {
        $id = $group.groupId
        Invoke-WorkflowUnit $id $suite ([string] $group.selector) $true
    }
}
$status = [ordered]@{ schemaVersion = 1; sourceRevision = $env:GITHUB_SHA
    sourceManifestSha256 = Get-FcHash $SourceManifestPath; runs = $rows.ToArray() }
$bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json $status -Depth 8) + "`n")
if ($bytes.Length -gt $script:FcContributors.UnitRunStatusMaximumBytes) { throw 'Unit status exceeds frozen bound.' }
$statusPath = Join-Path $EvidenceRoot 'functional-coverage.unit-run-status.json'
$stream = [IO.File]::Open($statusPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
finally { $stream.Dispose() }
& node @common @coverage '--KeyLoadTests:Suite=recovery' '--KeyLoadTests:Filter=/*/*/CommandIdempotencyProcessRecoveryTests/*' `
    ('--KeyLoadTests:ResultsDirectory=' + (Join-Path $EvidenceRoot 'recovery')) `
    ('--KeyLoadTests:CoverageOutput=' + (Join-Path $EvidenceRoot 'recovery/coverage.coverage'))
$recoveryExit = [int] $LASTEXITCODE
"recovery_exit_code=$recoveryExit" | Add-Content $env:GITHUB_OUTPUT
$clusterFilter = '/*/*/(PartitionQueryPublicClusterTests|McpDocumentCrudParityTests|RelationalSqlClusterJoinTests|RelationalSqlClusterJoinAuthorizationTests|RelationalSqlClusterJoinBudgetTests|RelationalSqlClusterJoinCancellationTests|RelationalSqlClusterJoinReadCutTests)/*'
& node @common @coverage '--KeyLoadTests:Suite=cluster' ('--KeyLoadTests:Filter=' + $clusterFilter) `
    ('--KeyLoadTests:ResultsDirectory=' + $EvidenceRoot) ('--KeyLoadTests:CoverageOutput=' + (Join-Path $EvidenceRoot 'coverage.coverage'))
$clusterExit = [int] $LASTEXITCODE
"cluster_exit_code=$clusterExit" | Add-Content $env:GITHUB_OUTPUT
if ($recoveryExit -ne 0 -or $clusterExit -ne 0 -or @($rows | Where-Object exitCode -NE 0).Count -ne 0) { exit 1 }
