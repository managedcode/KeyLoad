[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('prepare','verify')][string] $Mode,
    [Parameter(Mandatory = $true)][string] $Root,
    [Parameter(Mandatory = $true)][string] $EvidenceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'functional-coverage.shared.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compiled-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compile-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.inventory.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.test-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.production-snapshot.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.production-evidence.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.contributors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.product-contributors.ps1')

$script:Psm = [ordered]@{
    SchemaVersion = 3
    ManifestName = 'functional-coverage.production-source-manifest.json'
    MaximumSources = 5000
    MaximumEntries = 100000
    MaximumManifestBytes = 33554432
    MaximumPathCharacters = 4096
    InvalidRoot = 'The production identity root is absent, linked or unsupported.'
    InvalidInventory = 'The production project inventory is unsafe, oversized or ambiguous.'
    InvalidArtifact = 'An actual Release project DLL or PDB is absent or unsupported.'
    BindingProjectLabel = 'project='
    BindingMissingCountLabel = '; missingSourceCount='
    BindingMissingPathsLabel = '; firstMissingSourcePaths='
    BindingMissingPathsSeparator = ','
    MaximumBindingDiagnosticPaths = 8
    InvalidReceipt = 'The production source manifest or a referenced native image receipt is invalid.'
    Drift = 'A source, native image, compile input or producer changed during identity capture.'
    ProjectNames = @(
        'KeyLoad.Abstractions','KeyLoad.Analyzers','KeyLoad.AppHost','KeyLoad.Artifacts',
        'KeyLoad.Cli','KeyLoad.Client','KeyLoad.Core','KeyLoad.Diagnostics','KeyLoad.Orleans',
        'KeyLoad.Query','KeyLoad.Replication','KeyLoad.Security','KeyLoad.Server',
        'KeyLoad.ServiceDefaults','KeyLoad.Storage.IO','KeyLoad.Storage.ZoneTree'
    )
    TestProjects = @('KeyLoad.UnitTests','KeyLoad.RecoveryTests','KeyLoad.IntegrationTests')
    CentralNames = @('global.json','Directory.Build.props','Directory.Build.targets',
        'Directory.Packages.props','KeyLoad.slnx')
    ScriptExtensions = @('.ps1','.mjs','.sh','.xml','.json','.dockerfile')
    ScriptInventoryMaximumEntries = 5000
    ContractPath = 'scripts/Features/CodeQuality/functional-coverage.contract.json'
    ContributorRegistryPath = 'scripts/Features/CodeQuality/functional-coverage.product-contributors.json'
    MaximumJsonDepth = 32
    ProductionModuleRoster = @(
        'KeyLoad.Abstractions','KeyLoad.Artifacts','KeyLoad.Cli','KeyLoad.Client','KeyLoad.Core',
        'KeyLoad.Diagnostics','KeyLoad.Orleans','KeyLoad.Query','KeyLoad.Replication','KeyLoad.Security',
        'KeyLoad.Server','KeyLoad.ServiceDefaults','KeyLoad.Storage.IO','KeyLoad.Storage.ZoneTree'
    )
    SettingsPath = 'scripts/Features/CodeQuality/functional-coverage.production.settings.xml'
    SettingsEvidenceName = 'functional-coverage.production.settings.xml'
    ProducerPath = 'tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets'
    IdentityQualification = 'static native identity inspection only; no test, coverage or build qualification'
}

function Resolve-PsmRoot([string] $Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not [IO.Directory]::Exists($Path)) {
        throw $script:Psm.InvalidRoot
    }
    $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Path))
    if (((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw $script:Psm.InvalidRoot
    }
    $full
}

function Get-PsmCompiledSources([string] $Root, [string] $ProjectName, [bool] $IsTest, [string] $DllRelative) {
    $tree = if ($IsTest) { 'tests/' + $ProjectName } else { 'src/' + $ProjectName }
    $dllPath = Resolve-FcPath $Root $DllRelative
    $stream = [IO.File]::OpenRead($dllPath)
    $pe = $null
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $records = @(Read-FcAssemblyMetadataStrings $reader)
        $versions = @($records | Where-Object { $_.key -ceq $script:FcCompileIdentity.VersionKey })
        $counts = @($records | Where-Object { $_.key -ceq $script:FcCompileIdentity.SourceCountKey })
        if ($versions.Count -ne 1 -or $versions[0].value -cne $script:FcCompileIdentity.Version -or
            $counts.Count -ne 1 -or $counts[0].value -cnotmatch '\A[1-9][0-9]{0,3}\z') {
            throw $script:Psm.InvalidReceipt
        }
        $compiled = @(Read-FcCompileReceiptEntries $records $script:FcCompileIdentity.SourceKey)
        if ($compiled.Count -ne [int]$counts[0].value -or $compiled.Count -gt $script:Psm.MaximumSources) {
            throw $script:Psm.InvalidReceipt
        }
        $sourceHashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $compiled) {
            $relative = [string]$entry.path
            if (-not $relative.StartsWith($tree + '/', [StringComparison]::Ordinal) -or
                $sourceHashes.ContainsKey($relative)) { throw $script:Psm.InvalidReceipt }
            $path = Resolve-FcPath $Root $relative
            if (-not [IO.File]::Exists($path)) { throw $script:Psm.InvalidReceipt }
            $sourceHashes.Add($relative, (Get-FcHash $path))
        }
        $orderedPaths = [string[]]@($sourceHashes.Keys)
        [Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
        $ordered = [Collections.Generic.List[object]]::new()
        foreach ($relative in $orderedPaths) {
            $ordered.Add([ordered]@{ path = $relative; sha256 = $sourceHashes[$relative] })
        }
        @($ordered.ToArray())
    }
    finally {
        if ($null -ne $pe) { $pe.Dispose() }
        $stream.Dispose()
    }
}

function Assert-PsmSourceTree([string] $Root, [string] $ProjectName, [bool] $IsTest, [object[]] $CompiledSources) {
    $tree = if ($IsTest) { 'tests/' + $ProjectName } else { 'src/' + $ProjectName }
    $directory = Resolve-FcPath $Root $tree
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($directory)
    $compiled = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $CompiledSources) { [void]$compiled.Add([string]$source.path) }
    $entries = 0
    $sources = 0
    while ($pending.Count -gt 0) {
        foreach ($entryPath in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            if ($entries -ge $script:Psm.MaximumEntries) { throw $script:Psm.InvalidInventory }
            $entries++
            $entry = Get-Item -LiteralPath $entryPath -Force
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $script:Psm.InvalidInventory }
            if ($entry -is [IO.DirectoryInfo]) {
                if ($entry.Name -cne 'obj' -and $entry.Name -cne 'bin') { $pending.Push($entry.FullName) }
                continue
            }
            if ($entry -isnot [IO.FileInfo] -or $entry.Extension -cne '.cs') { continue }
            if ($sources -ge $script:Psm.MaximumSources) { throw $script:Psm.InvalidInventory }
            $sources++
            $relative = [IO.Path]::GetRelativePath($Root, $entry.FullName).Replace([IO.Path]::DirectorySeparatorChar, '/')
            if (-not $compiled.Remove($relative)) { throw $script:Psm.InvalidInventory }
        }
    }
    if ($sources -eq 0 -or $compiled.Count -ne 0) { throw $script:Psm.InvalidInventory }
}

function Get-PsmCentralInputs([string] $Root, [string] $ProjectName, [bool] $IsTest) {
    $prefix = if ($IsTest) { 'tests/' } else { 'src/' }
    $projectPath = $prefix + $ProjectName + '/' + $ProjectName + '.csproj'
    $inputs = [Collections.Generic.List[object]]::new()
    foreach ($relative in @($script:Psm.CentralNames) + @($projectPath)) {
        $path = Resolve-FcPath $Root $relative
        if (-not [IO.File]::Exists($path)) { throw $script:Psm.InvalidInventory }
        $inputs.Add([ordered]@{ path = $relative; sha256 = Get-FcHash $path })
    }
    @($inputs.ToArray())
}

function Get-PsmProjectIdentity([string] $Root, [string] $ProjectName, [bool] $IsTest) {
    $tree = if ($IsTest) { 'tests/' + $ProjectName } else { 'src/' + $ProjectName }
    $dll = $tree + '/bin/Release/net10.0/' + $ProjectName + '.dll'
    $pdb = $tree + '/bin/Release/net10.0/' + $ProjectName + '.pdb'
    $sources = @(Get-PsmCompiledSources $Root $ProjectName $IsTest $dll)
    Assert-PsmSourceTree $Root $ProjectName $IsTest $sources
    $central = @(Get-PsmCentralInputs $Root $ProjectName $IsTest)
    foreach ($relative in @($dll,$pdb)) {
        $path = Resolve-FcPath $Root $relative
        if (-not [IO.File]::Exists($path)) { throw $script:Psm.InvalidArtifact }
    }
    $identity = Read-FcCompiledIdentity $Root $dll $pdb $sources
    if ($identity.moduleName -cne ($ProjectName + '.dll')) {
        throw $script:Psm.InvalidArtifact
    }
    if (-not $identity.compiledSourceBindingComplete) {
        $missingSources = @($identity.sourceFilesWithoutPdbDocuments)
        $missingPaths = @($missingSources | Select-Object -First $script:Psm.MaximumBindingDiagnosticPaths |
            ForEach-Object { [string] $_.path })
        throw ($script:Psm.InvalidArtifact + ' ' + $script:Psm.BindingProjectLabel + $ProjectName +
            $script:Psm.BindingMissingCountLabel + [string] $missingSources.Count +
            $script:Psm.BindingMissingPathsLabel + ($missingPaths -join $script:Psm.BindingMissingPathsSeparator))
    }
    $producerPath = Resolve-FcPath $Root $script:Psm.ProducerPath
    $producer = [ordered]@{ path = $script:Psm.ProducerPath; sha256 = Get-FcHash $producerPath }
    $receipt = Read-FcNativeCompileReceipt (Resolve-FcPath $Root $dll) $sources $central $producer
    $identity['compileReceipt'] = $receipt
    [ordered]@{ sources = $sources; compiledIdentity = $identity }
}

function Get-PsmProductRows([string] $Root) {
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($name in $script:Psm.ProjectNames) {
        $identity = Get-PsmProjectIdentity $Root $name $false
        $role = if ($name -ceq 'KeyLoad.AppHost' -or $name -ceq 'KeyLoad.Analyzers') { 'infrastructure' } else { 'production' }
        $rows.Add([ordered]@{ module = $name; role = $role; sources = $identity.sources; compiledIdentity = $identity.compiledIdentity })
    }
    @($rows.ToArray())
}

function Get-PsmTestImage([string] $Root, [string] $ProjectName) {
    $identity = Get-PsmProjectIdentity $Root $ProjectName $true
    [ordered]@{
        schemaVersion = 1
        project = 'tests/' + $ProjectName + '/' + $ProjectName + '.csproj'
        sources = $identity.sources
        buildInputs = @(Get-PsmCentralInputs $Root $ProjectName $true)
        compiledIdentity = $identity.compiledIdentity
        qualification = $script:Psm.IdentityQualification
    }
}

function ConvertTo-PsmJsonBytes([object] $Value) {
    $text = ConvertTo-Json -InputObject $Value -Depth 30
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text + "`n")
    if ($bytes.Length -gt $script:Psm.MaximumManifestBytes) { throw $script:Psm.InvalidReceipt }
    ,$bytes
}

function Get-PsmCaseInventory([string] $Root) {
    @(Read-PsmProductContributorRegistry $Root)
}

function Get-PsmScriptInventory([string] $Root) {
    $directory = Resolve-FcPath $Root 'scripts/Features/CodeQuality'
    $names = [Collections.Generic.List[string]]::new()
    $count = 0
    foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
        if ($count -ge $script:Psm.ScriptInventoryMaximumEntries) { throw $script:Psm.InvalidInventory }
        $count++
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $item -isnot [IO.FileInfo]) { throw $script:Psm.InvalidInventory }
        $include = $item.Name -ceq '.dockerignore'
        if ($item.Name.StartsWith('functional-coverage', [StringComparison]::Ordinal)) {
            $include = $false
            foreach ($extension in $script:Psm.ScriptExtensions) {
                if ($item.Name.EndsWith($extension, [StringComparison]::Ordinal)) { $include = $true; break }
            }
            if (-not $include) { throw $script:Psm.InvalidInventory }
        }
        if (-not $include) { continue }
        if ([IO.Path]::GetFileName($item.Name) -cne $item.Name -or
            $item.Name -match '[\\/]' -or $item.Length -gt $script:Psm.MaximumManifestBytes) { throw $script:Psm.InvalidInventory }
        $names.Add($item.Name)
    }
    if ($names.Count -eq 0) { throw $script:Psm.InvalidInventory }
    $orderedNames = [string[]]$names.ToArray()
    [Array]::Sort($orderedNames, [StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($name in $orderedNames) {
        if (-not $seen.Add($name)) { throw $script:Psm.InvalidInventory }
        $relative = 'scripts/Features/CodeQuality/' + $name
        $rows.Add([ordered]@{ name = $name; sha256 = Get-FcHash (Resolve-FcPath $Root $relative) })
    }
    return $rows.ToArray()
}

function Get-PsmManifest([string] $Root, [object[]] $ScriptInventory) {
    $contractPath = Resolve-FcPath $Root $script:Psm.ContractPath
    $contract = Read-FcContract $contractPath
    $settingsPath = Resolve-FcPath $Root $script:Psm.SettingsPath
    $settingsBytes = Read-PsmBounded $settingsPath
    $products = @(Get-PsmProductRows $Root)
    if ($products.Count -ne 16) { throw $script:Psm.InvalidReceipt }
    $producerPath = Resolve-FcPath $Root $script:Psm.ProducerPath
    $producer = [ordered]@{ path = $script:Psm.ProducerPath; sha256 = Get-FcHash $producerPath }
    $testImages = [ordered]@{}
    foreach ($project in $script:Psm.TestProjects) { $testImages[$project] = Get-PsmTestImage $Root $project }
    $references = [Collections.Generic.List[object]]::new()
    $files = [Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
    $sidecarBytes = 0L
    foreach ($item in @(
        [ordered]@{ suite = 'unit'; project = 'KeyLoad.UnitTests'; file = 'functional-coverage.test-image.unit.json' },
        [ordered]@{ suite = 'recovery'; project = 'KeyLoad.RecoveryTests'; file = 'functional-coverage.test-image.recovery.json' },
        [ordered]@{ suite = 'rf3'; project = 'KeyLoad.IntegrationTests'; file = 'functional-coverage.test-image.rf3.json' }
    )) {
        $bytes = ConvertTo-PsmJsonBytes $testImages[$item.project]
        if ($sidecarBytes -gt ($script:Psm.MaximumManifestBytes - $bytes.Length)) { throw $script:Psm.InvalidReceipt }
        $sidecarBytes += $bytes.Length
        $files.Add($item.file, $bytes)
        $hash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))
        $references.Add([ordered]@{ suite = $item.suite; fileName = $item.file; sha256 = $hash })
    }
    if ($sidecarBytes -gt ($script:Psm.MaximumManifestBytes - $settingsBytes.Length)) { throw $script:Psm.InvalidReceipt }
    $sidecarBytes += $settingsBytes.Length
    $files.Add($script:Psm.SettingsEvidenceName, $settingsBytes)
    $contractHash = Get-FcHash $contractPath
    $manifest = [ordered]@{
        schemaVersion = $script:Psm.SchemaVersion
        sourceRevision = Get-FcRevision $Root
        repository = 'managedcode/KeyLoad'
        contractSha256 = $contractHash
        compiledProducts = $products
        compiledTestsManifest = @(
            [ordered]@{ suite = 'unit'; fileName = 'functional-coverage.test-image.unit.json'; sha256 = $references[0].sha256 },
            [ordered]@{ suite = 'unit-scalar'; fileName = 'functional-coverage.test-image.unit.json'; sha256 = $references[0].sha256 },
            [ordered]@{ suite = 'recovery'; fileName = 'functional-coverage.test-image.recovery.json'; sha256 = $references[1].sha256 },
            [ordered]@{ suite = 'rf3'; fileName = 'functional-coverage.test-image.rf3.json'; sha256 = $references[2].sha256 }
        )
        compilationProducer = $producer
        contributors = @(Get-PsmCaseInventory $Root)
        settingsSha256 = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($settingsBytes))
        scripts = $ScriptInventory
    }
    [ordered]@{ manifest = $manifest; files = $files }
}

function Write-PsmCreateOnly([string] $Path, [byte[]] $Bytes) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}

function Read-PsmBounded([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item -isnot [IO.FileInfo] -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw $script:Psm.InvalidReceipt
    }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $memory = [IO.MemoryStream]::new()
    $buffer = [byte[]]::new(8192)
    try {
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($memory.Length -gt ($script:Psm.MaximumManifestBytes - $read)) { throw $script:Psm.InvalidReceipt }
            $memory.Write($buffer, 0, $read)
        }
        ,$memory.ToArray()
    }
    finally { $memory.Dispose(); $stream.Dispose() }
}

function Test-PsmBytesEqual([byte[]] $Left, [byte[]] $Right) {
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) { return $false }
    }
    return $true
}

function Assert-PsmNoLinks([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $current = [IO.Path]::GetPathRoot($full)
    foreach ($segment in $full.Substring($current.Length).Split(
            [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw $script:Psm.InvalidRoot
            }
        }
    }
}

function Invoke-Psm([string] $Root, [string] $EvidenceRoot, [string] $Operation) {
    $rootPath = Resolve-PsmRoot $Root
    if (-not [IO.Path]::IsPathFullyQualified($EvidenceRoot)) { throw $script:Psm.InvalidRoot }
    $evidence = [IO.Path]::GetFullPath($EvidenceRoot)
    Assert-PsmNoLinks $evidence
    if ($Operation -ceq 'prepare') {
        [IO.Directory]::CreateDirectory($evidence) | Out-Null
    }
    elseif (-not [IO.Directory]::Exists($evidence)) { throw $script:Psm.InvalidReceipt }
    if (((Get-Item -LiteralPath $evidence -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw $script:Psm.InvalidRoot
    }
    $scriptInventory = @(Get-PsmScriptInventory $rootPath)
    $expected = Get-PsmManifest $rootPath $scriptInventory
    $scriptInventoryAfter = @(Get-PsmScriptInventory $rootPath)
    Assert-FcScriptInventoryUnchanged $scriptInventory $scriptInventoryAfter
    $rechecked = Get-PsmManifest $rootPath $scriptInventoryAfter
    $manifestBytes = ConvertTo-PsmJsonBytes $expected.manifest
    $recheckedManifestBytes = ConvertTo-PsmJsonBytes $rechecked.manifest
    Assert-FcProductionSnapshotEqual $manifestBytes $recheckedManifestBytes $expected.files $rechecked.files
    $manifestPath = Join-Path $evidence $script:Psm.ManifestName
    if ($Operation -ceq 'prepare') {
        Publish-PsmEvidenceCreateOnly $evidence $manifestPath $manifestBytes $expected.files
        return
    }
    Assert-PsmEvidenceMatches $evidence $manifestPath $manifestBytes $expected.files
}

Invoke-Psm $Root $EvidenceRoot $Mode
