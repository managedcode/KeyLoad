param([Parameter(Mandatory)][string] $Repository, [Parameter(Mandatory)][string] $EvidenceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
foreach ($name in @('shared','compiled-identity','compile-identity','test-identity',
        'native-merge.inputs','native-merge.files','native-merge.process','native-merge.contributors',
        'native-merge.trx','native-merge.unit-selectors','native-merge.unit-inventory')) {
    . (Join-Path $PSScriptRoot ('functional-coverage.' + $name + '.ps1'))
}
$script:FcNativeMergeInput.ReadBufferBytes = 65536
$script:FcNativeMergeInput['SettlementTimeoutSeconds'] = 30

function Write-WorkflowOriginal([string] $Path, [string] $Text) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}

function Read-WorkflowOriginalMetadata([string] $Dll) {
    $stream = [IO.File]::OpenRead($Dll)
    $pe = $null
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        @(Read-FcAssemblyMetadataStrings $reader)
    }
    finally { if ($null -ne $pe) { $pe.Dispose() }; $stream.Dispose() }
}

function Get-WorkflowNativeObservation([string] $Root) {
    $snapshot = Get-FcTestIdentitySnapshot $Root
    $compiled = $snapshot.compiledIdentity
    $dll = Resolve-FcPath $Root $script:FcTestIdentity.Dll
    $metadata = @(Read-WorkflowOriginalMetadata $dll)
    if ((Get-FcHash $dll) -cne $compiled.dllSha256) { throw $script:FcTestIdentity.Drift }
    [ordered]@{ nativeImage = $compiled; nativeCompileReceipt = $compiled.compileReceipt
        declaredSources = $snapshot.sources; originalNativeMetadataRecords = $metadata
        independentSnapshot = $snapshot; sourceDeclarationScope = 'native UnitTests compilation and portable PDB'
        qualification = 'native metadata observation only; no successful execution or coverage implied' }
}

function Invoke-WorkflowDiscovery([string] $Root, [string] $Evidence, [object] $Inventory,
    [string] $Suite, [object] $Group) {
    $groupId = if ($null -eq $Group) { '' } else { [string] $Group.groupId }
    $id = $Suite + '-discovery' + $(if ($groupId -ceq '') { '' } else { '-' + $groupId })
    $directory = Join-Path $Evidence $id
    if ([IO.Directory]::Exists($directory)) { throw 'Native discovery evidence must be fresh.' }
    [void] [IO.Directory]::CreateDirectory($directory)
    $before = Get-WorkflowNativeObservation $Root
    $observationPath = Join-Path $directory 'native-image-observation.json'
    Write-WorkflowOriginal $observationPath (ConvertTo-Json $before -Depth 32)
    $dll = Resolve-FcPath $Root $script:FcTestIdentity.Dll
    $arguments = @($dll,'--list-tests','json','--disable-logo','--ansi','off','--progress','off',
        '--output','Detailed','--results-directory',(Join-Path $directory 'native'))
    if ($null -ne $Group) { $arguments += @('--treenode-filter',[string] $Group.selector) }
    $original = Invoke-FcCoverageProcess 'dotnet' ([string[]] $arguments) 1800 33554432
    $censusPath = Join-Path $directory 'native-list.json'
    Write-WorkflowOriginal $censusPath ([string] $original.stdout)
    Write-WorkflowOriginal (Join-Path $directory 'native-list.stderr.txt') ([string] $original.stderr)
    Write-WorkflowOriginal (Join-Path $directory 'native-process.json') (ConvertTo-Json ([ordered]@{ suite = $Suite; groupId = $groupId; arguments = $arguments; timeoutSeconds = 1800; settlementSeconds = 30; nativeResult = $original }) -Depth 12)
    $after = Get-WorkflowNativeObservation $Root
    Write-WorkflowOriginal (Join-Path $directory 'native-image-after.json') (ConvertTo-Json $after -Depth 32)
    if ((ConvertTo-Json $before -Depth 32 -Compress) -cne (ConvertTo-Json $after -Depth 32 -Compress) -or
        $original.failures.Count -ne 0 -or $original.exitCode -ne 0 -or -not $original.exitJoined -or
        -not $original.outputJoined -or -not $original.errorJoined -or -not $original.disposed) {
        throw 'Native discovery failed or source/image drifted; original evidence retained.'
    }
    $parameters = @{ Repository = $Root; CensusPath = $censusPath; CensusSha256 = Get-FcHash $censusPath
        InventorySha256 = Get-FcHash (Join-Path $PSScriptRoot $script:FcNativeContributors.UnitInventoryName)
        AssemblyFullName = [Reflection.AssemblyName]::GetAssemblyName($dll).FullName
        ImageObservationPath = $observationPath; ImageObservationSha256 = Get-FcHash $observationPath }
    if ($groupId -cne '') { $parameters.GroupId = $groupId }
    & (Join-Path $PSScriptRoot 'functional-coverage.native-census.ps1') @parameters
}

$Repository = [IO.Path]::GetFullPath($Repository)
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$relativeEvidence = [IO.Path]::GetRelativePath($Repository, $EvidenceRoot).Replace('\', '/')
if ((Resolve-FcPath $Repository $relativeEvidence) -cne $EvidenceRoot) { throw 'Evidence root must be confined and existing.' }
Assert-FcNativeNoReparsePath $EvidenceRoot
$inventoryPath = Resolve-FcPath $Repository ('scripts/Features/CodeQuality/' + $script:FcNativeContributors.UnitInventoryName)
$read = Read-FcNativeBoundedFile $inventoryPath 33554432 65536 $script:FcNativeContributors.Invalid
$json = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($read.bytes))
try { Assert-FcNativeJsonUnique $json.RootElement }
finally { $json.Dispose() }
$inventory = ConvertFrom-Json ([Text.Encoding]::UTF8.GetString($read.bytes)) -AsHashtable -Depth 64
$inherited = [Environment]::GetEnvironmentVariable('DOTNET_EnableHWIntrinsic')
try {
    foreach ($suite in @('unit','unit-scalar')) {
        [Environment]::SetEnvironmentVariable('DOTNET_EnableHWIntrinsic', $(if ($suite -ceq 'unit-scalar') { '0' } else { $inherited }))
        Invoke-WorkflowDiscovery $Repository $EvidenceRoot $inventory $suite $null
        foreach ($group in $inventory.coverageGroups) {
            Invoke-WorkflowDiscovery $Repository $EvidenceRoot $inventory $suite $group
        }
    }
}
finally { [Environment]::SetEnvironmentVariable('DOTNET_EnableHWIntrinsic', $inherited) }
