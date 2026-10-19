param(
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $CensusPath,
    [Parameter(Mandatory)][string] $CensusSha256,
    [Parameter(Mandatory)][string] $InventorySha256,
    [Parameter(Mandatory)][string] $AssemblyFullName,
    [Parameter(Mandatory)][string] $ImageObservationPath,
    [Parameter(Mandatory)][string] $ImageObservationSha256,
    [string] $GroupId = '',
    [string] $RebindOutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'functional-coverage.shared.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.inputs.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.files.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.contributors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.trx.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.unit-selectors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-merge.unit-inventory.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.execution-census.rebind.ps1')

# Shared source-line readers require the same frozen 64 KiB execution census read buffer.
$script:FcMergeInput.ReadBufferBytes = 65536

function Read-FcCensusJson([string] $Path, [string] $ExpectedHash) {
    if ($ExpectedHash -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcContributors.Invalid }
    $read = Read-FcBoundedFile $Path 33554432 65536 $script:FcContributors.Invalid
    if ($read.sha256 -cne $ExpectedHash) { throw $script:FcContributors.Invalid }
    $document = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($read.bytes))
    try {
        Assert-FcJsonUnique $document.RootElement
        ConvertFrom-Json ([Text.Encoding]::UTF8.GetString($read.bytes)) -AsHashtable -Depth 64
    }
    finally { $document.Dispose() }
}

function Get-FcObservedUnitImages([object] $Observation, [string] $Root) {
    Assert-FcContributorExactKeys $Observation @('runtimeImage','runtimeCompileReceipt','declaredSources',
        'originalMetadataRecords','independentSnapshot','sourceDeclarationScope','qualification')
    $image = $Observation.runtimeImage
    if ($image.compiledSourceBindingComplete -isnot [bool] -or -not $image.compiledSourceBindingComplete -or
        $image.mvid -isnot [string] -or $image.mvid -cnotmatch '\A[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\z' -or
        $Observation.declaredSources -isnot [array] -or $Observation.declaredSources.Count -eq 0 -or
        $Observation.declaredSources.Count -gt 8192 -or
        ($Observation.runtimeCompileReceipt.sourceCount -isnot [int] -and
            $Observation.runtimeCompileReceipt.sourceCount -isnot [long]) -or
        $Observation.runtimeCompileReceipt.sourceCount -ne $Observation.declaredSources.Count) {
        throw $script:FcContributors.Invalid
    }
    foreach ($name in @('dll','pdb')) {
        $expected = 'tests/KeyLoad.UnitTests/bin/Debug/net10.0/KeyLoad.UnitTests.' + $name
        if ($image[$name] -cne $expected -or $image[$name + 'Sha256'] -cnotmatch '\A[0-9a-f]{64}\z') {
            throw $script:FcContributors.Invalid
        }
        $path = Resolve-FcPath $Root $expected
        $read = Read-FcHashFile $path 268435456 65536 $script:FcContributors.Invalid
        if ($read.sha256 -cne $image[$name + 'Sha256']) { throw $script:FcContributors.Invalid }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Observation.declaredSources) {
        Assert-FcContributorExactKeys $source @('path','sha256')
        if ($source.path -isnot [string] -or $source.path -cnotmatch
            '\Atests/KeyLoad\.UnitTests/(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.cs\z' -or
            $source.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or -not $seen.Add($source.path)) {
            throw $script:FcContributors.Invalid
        }
        $path = Resolve-FcPath $Root $source.path
        $read = Read-FcHashFile $path 33554432 65536 $script:FcContributors.Invalid
        if ($read.sha256 -cne $source.sha256) { throw $script:FcContributors.Invalid }
    }
    @{ unit = @{ identity = @{ manifest = @{ sources = $Observation.declaredSources } } } }
}

$Repository = [IO.Path]::GetFullPath($Repository)
$rootInfo = Get-Item -LiteralPath $Repository -Force
if ($rootInfo -isnot [IO.DirectoryInfo] -or ($rootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw $script:FcContributors.Invalid
}
$inventoryPath = Resolve-FcPath $Repository ('scripts/Features/CodeQuality/' +
    $script:FcContributors.UnitInventoryName)
$inventory = Read-FcCensusJson $inventoryPath $InventorySha256
$observation = Read-FcCensusJson $ImageObservationPath $ImageObservationSha256
$images = Get-FcObservedUnitImages $observation $Repository
$bounds = @{ maximumFileBytes = 33554432 }
Assert-FcUnitInventoryShape $Repository $inventory $images $bounds
$census = Read-FcCensusJson $CensusPath $CensusSha256
if ($GroupId -ceq '') {
    $expected = [string[]] @(@($inventory.functionalCases) + @($inventory.requiredNonContributorCases) |
        ForEach-Object { Get-FcTrxKey $_.className $_.methodName $_.instanceName })
}
else {
    if ($GroupId -cnotmatch '\Aunit-functional-0[1-5]\z') { throw $script:FcContributors.Invalid }
    $groups = @($inventory.coverageGroups | Where-Object { $_.groupId -ceq $GroupId })
    if ($groups.Count -ne 1) { throw $script:FcContributors.Invalid }
    $expected = [string[]] $groups[0].caseIdentities
}
if ($RebindOutputDirectory -cne '') {
    if ($GroupId -cne '') { throw $script:FcContributors.Invalid }
    $inventory = Get-FcRangeReboundInventory $inventory $census $Repository $AssemblyFullName $images $bounds
    Write-FcPrivateReboundInventory $inventory $RebindOutputDirectory $Repository
}
Assert-FcObservedCensus $census $inventory $Repository $AssemblyFullName $expected
Write-Output ('Execution discovery identities equal reviewed selection: ' + $expected.Count)
