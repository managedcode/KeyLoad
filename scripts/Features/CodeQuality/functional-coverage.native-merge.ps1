[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Product','ProductAdmission','ToolingProof')][string] $Mode,
    [Parameter(Mandatory = $true)][string] $Repository,
    [Parameter(Mandatory = $true)][string] $EvidenceRoot,
    [string] $DescriptorPath,
    [string] $ToolPackageRoot,
    [string] $ToolVersion,
    [string] $NativeOptionsJson,
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
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.inputs.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.test-images.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.contributors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.unit-inventory.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.unit-selectors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.trx.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.functional-report.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.files.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.process.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.counts.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.context.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.server-closure.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.sources.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.fixture-receipt.ps1')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.admission.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.execution.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.tooling.ps1')

try {
    if ($Mode -ceq 'ToolingProof') {
        Invoke-FcNativeToolingProof | ConvertTo-Json -Compress -Depth 8 | Write-Output
        exit 0
    }
    if ($Mode -cnotin @('Product','ProductAdmission')) { throw 'Tooling-proof mode is supplied by the TUnit operation helper only.' }
    if (-not [IO.Path]::IsPathFullyQualified($Repository) -or -not [IO.Path]::IsPathFullyQualified($EvidenceRoot) -or
        -not [IO.Path]::IsPathFullyQualified($DescriptorPath) -or [string]::IsNullOrWhiteSpace($NativeOptionsJson)) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    $Repository = [IO.Path]::GetFullPath($Repository)
    $EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
    $ToolPackageRoot = [IO.Path]::GetFullPath($ToolPackageRoot)
    Assert-FcNativeNoReparsePath $Repository
    Assert-FcNativeNoReparsePath $EvidenceRoot
    Assert-FcNativeNoReparsePath $ToolPackageRoot
    $expectedBounds = ConvertFrom-Json -InputObject $NativeOptionsJson -AsHashtable -Depth 4
    $plan = Read-FcNativeProductPlan $EvidenceRoot $Repository $DescriptorPath $expectedBounds
    if ($Mode -ceq 'ProductAdmission') {
        [ordered]@{ admitted = $true; invocationId = $plan.descriptor.invocationId
            sourceRevision = $plan.manifest.sourceRevision; inputCount = $plan.inputs.Count
            suiteRunCount = $plan.descriptor.suiteRuns.Count } | ConvertTo-Json -Compress -Depth 4 | Write-Output
        exit 0
    }
    Invoke-FcNativeMergePlan $plan | ConvertTo-Json -Compress -Depth 8 | Write-Output
}
catch [System.Exception] {
    [Console]::Error.WriteLine($_.Exception.Message)
    $location = $_.InvocationInfo
    if ($null -ne $location) {
        [Console]::Error.WriteLine(('{0}:{1}' -f [IO.Path]::GetFileName($location.ScriptName), $location.ScriptLineNumber))
    }
    exit 1
}
