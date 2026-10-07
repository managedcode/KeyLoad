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
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.inputs.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.files.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.contributors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.trx.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.unit-selectors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.unit-inventory.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-census.rebind.ps1')

# Shared source-line readers require the same frozen 64 KiB native census read buffer.
$script:FcNativeMergeInput.ReadBufferBytes = 65536

function Read-FcNativeCensusJson([string] $Path, [string] $ExpectedHash) {
    if ($ExpectedHash -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeContributors.Invalid }
    $read = Read-FcNativeBoundedFile $Path 33554432 65536 $script:FcNativeContributors.Invalid
    if ($read.sha256 -cne $ExpectedHash) { throw $script:FcNativeContributors.Invalid }
    $document = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($read.bytes))
    try {
        Assert-FcNativeJsonUnique $document.RootElement
        ConvertFrom-Json ([Text.Encoding]::UTF8.GetString($read.bytes)) -AsHashtable -Depth 64
    }
    finally { $document.Dispose() }
}

function Get-FcNativeObservedUnitImages([object] $Observation, [string] $Root) {
    Assert-FcNativeContributorExactKeys $Observation @('nativeImage','nativeCompileReceipt','declaredSources',
        'originalNativeMetadataRecords','independentSnapshot','sourceDeclarationScope','qualification')
    $image = $Observation.nativeImage
    if ($image.compiledSourceBindingComplete -isnot [bool] -or -not $image.compiledSourceBindingComplete -or
        $image.mvid -isnot [string] -or $image.mvid -cnotmatch '\A[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\z' -or
        $Observation.declaredSources -isnot [array] -or $Observation.declaredSources.Count -eq 0 -or
        $Observation.declaredSources.Count -gt 8192 -or
        ($Observation.nativeCompileReceipt.sourceCount -isnot [int] -and
            $Observation.nativeCompileReceipt.sourceCount -isnot [long]) -or
        $Observation.nativeCompileReceipt.sourceCount -ne $Observation.declaredSources.Count) {
        throw $script:FcNativeContributors.Invalid
    }
    foreach ($name in @('dll','pdb')) {
        $expected = 'tests/KeyLoad.UnitTests/bin/Release/net10.0/KeyLoad.UnitTests.' + $name
        if ($image[$name] -cne $expected -or $image[$name + 'Sha256'] -cnotmatch '\A[0-9a-f]{64}\z') {
            throw $script:FcNativeContributors.Invalid
        }
        $path = Resolve-FcPath $Root $expected
        $read = Read-FcNativeHashFile $path 268435456 65536 $script:FcNativeContributors.Invalid
        if ($read.sha256 -cne $image[$name + 'Sha256']) { throw $script:FcNativeContributors.Invalid }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Observation.declaredSources) {
        Assert-FcNativeContributorExactKeys $source @('path','sha256')
        if ($source.path -isnot [string] -or $source.path -cnotmatch
            '\Atests/KeyLoad\.UnitTests/(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.cs\z' -or
            $source.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or -not $seen.Add($source.path)) {
            throw $script:FcNativeContributors.Invalid
        }
        $path = Resolve-FcPath $Root $source.path
        $read = Read-FcNativeHashFile $path 33554432 65536 $script:FcNativeContributors.Invalid
        if ($read.sha256 -cne $source.sha256) { throw $script:FcNativeContributors.Invalid }
    }
    @{ unit = @{ identity = @{ manifest = @{ sources = $Observation.declaredSources } } } }
}

$Repository = [IO.Path]::GetFullPath($Repository)
$rootInfo = Get-Item -LiteralPath $Repository -Force
if ($rootInfo -isnot [IO.DirectoryInfo] -or ($rootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw $script:FcNativeContributors.Invalid
}
$inventoryPath = Resolve-FcPath $Repository ('scripts/Features/CodeQuality/' +
    $script:FcNativeContributors.UnitInventoryName)
$inventory = Read-FcNativeCensusJson $inventoryPath $InventorySha256
$observation = Read-FcNativeCensusJson $ImageObservationPath $ImageObservationSha256
$images = Get-FcNativeObservedUnitImages $observation $Repository
$bounds = @{ maximumFileBytes = 33554432 }
Assert-FcNativeUnitInventoryShape $Repository $inventory $images $bounds
$census = Read-FcNativeCensusJson $CensusPath $CensusSha256
if ($GroupId -ceq '') {
    $expected = [string[]] @(@($inventory.functionalCases) + @($inventory.requiredNonContributorCases) |
        ForEach-Object { Get-FcNativeTrxKey $_.className $_.methodName $_.instanceName })
}
else {
    if ($GroupId -cnotmatch '\Aunit-functional-0[1-5]\z') { throw $script:FcNativeContributors.Invalid }
    $groups = @($inventory.coverageGroups | Where-Object { $_.groupId -ceq $GroupId })
    if ($groups.Count -ne 1) { throw $script:FcNativeContributors.Invalid }
    $expected = [string[]] $groups[0].caseIdentities
}
if ($RebindOutputDirectory -cne '') {
    if ($GroupId -cne '') { throw $script:FcNativeContributors.Invalid }
    $inventory = Get-FcNativeRangeReboundInventory $inventory $census $Repository $AssemblyFullName $images $bounds
    Write-FcNativePrivateReboundInventory $inventory $RebindOutputDirectory $Repository
}
Assert-FcNativeObservedCensus $census $inventory $Repository $AssemblyFullName $expected
Write-Output ('Native discovery identities equal reviewed selection: ' + $expected.Count)
