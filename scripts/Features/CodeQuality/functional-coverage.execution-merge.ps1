[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Product','ProductAdmission','ToolingProof')][string] $Mode,
    [Parameter(Mandatory = $true)][string] $Repository,
    [Parameter(Mandatory = $true)][string] $EvidenceRoot,
    [string] $DescriptorPath,
    [string] $ToolPackageRoot,
    [string] $ToolVersion,
    [string] $OptionsJson,
    [string] $OutputDirectory,
    [string] $ToolingInputDescriptor,
    [long] $MaximumDescriptorBytes = 0,
    [int] $MaximumFiles = 0,
    [int] $TimeoutSeconds = 0,
    [int] $ReadBufferBytes = 0,
    [long] $MaximumTotalBytes = 0,
    [long] $MaximumFileBytes = 0,
    [int] $MaximumPathCharacters = 0,
    [long] $MaximumManifestBytes = 0,
    [long] $MaximumReportBytes = 0,
    [int] $SettlementTimeoutSeconds = 0,
    [int] $MaximumOutputCharacters = 0
)

. (Join-Path $PSScriptRoot 'functional-coverage.shared.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compiled-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compile-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.test-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.image-manifests.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.inputs.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.test-images.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.contributors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.unit-inventory.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.unit-selectors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.trx.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.functional-report.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.files.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.process.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.counts.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.sources.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.process-lifecycle.ps1') -Repository $Repository `
    -ResultsDirectory $EvidenceRoot -SourceManifestPath $DescriptorPath
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.admission.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.execution.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.tooling.ps1')

try {
    if ($Mode -ceq 'ToolingProof') {
        Invoke-FcToolingProof | ConvertTo-Json -Compress -Depth 8 | Write-Output
        exit 0
    }
    if ($Mode -cnotin @('Product','ProductAdmission')) { throw 'Tooling-proof mode is supplied by the TUnit operation helper only.' }
    if (-not [IO.Path]::IsPathFullyQualified($Repository) -or -not [IO.Path]::IsPathFullyQualified($EvidenceRoot) -or
        -not [IO.Path]::IsPathFullyQualified($DescriptorPath) -or [string]::IsNullOrWhiteSpace($OptionsJson)) { throw $script:FcMergeInput.InvalidDescriptor }
    $Repository = [IO.Path]::GetFullPath($Repository)
    $EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
    $ToolPackageRoot = [IO.Path]::GetFullPath($ToolPackageRoot)
    Assert-FcNoReparsePath $Repository
    Assert-FcNoReparsePath $EvidenceRoot
    Assert-FcNoReparsePath $ToolPackageRoot
    $expectedBounds = ConvertFrom-Json -InputObject $OptionsJson -AsHashtable -Depth 4
    $plan = Read-FcProductPlan $EvidenceRoot $Repository $DescriptorPath $expectedBounds
    if ($Mode -ceq 'ProductAdmission') {
        throw $script:FcMergeInput.ProcessAttributionOpen
    }
    Invoke-FcMergePlan $plan | ConvertTo-Json -Compress -Depth 8 | Write-Output
}
catch [System.Exception] {
    [Console]::Error.WriteLine($_.Exception.Message)
    $location = $_.InvocationInfo
    if ($null -ne $location) {
        [Console]::Error.WriteLine(('{0}:{1}' -f [IO.Path]::GetFileName($location.ScriptName), $location.ScriptLineNumber))
    }
    exit 1
}
