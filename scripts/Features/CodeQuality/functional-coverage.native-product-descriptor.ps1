[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Repository,
    [Parameter(Mandatory = $true)][string] $SourceManifestPath,
    [Parameter(Mandatory = $true)][string] $ResultsRoot,
    [Parameter(Mandatory = $true)][string] $UnitRunStatusPath,
    [Parameter(Mandatory = $true)][int] $RecoveryExitCode,
    [Parameter(Mandatory = $true)][int] $ClusterExitCode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'functional-coverage.shared.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compiled-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compile-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.test-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.image-manifests.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.process-lifecycle.ps1') -Repository $Repository `
    -ResultsDirectory $ResultsRoot -SourceManifestPath $SourceManifestPath
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.inputs.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.files.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.process.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.test-images.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.contributors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.unit-inventory.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.unit-selectors.ps1')

$script:FcNativeProductDescriptor = [ordered]@{
    Invalid = 'The original functional coverage inputs are incomplete or mismatched.'
    SourceName = 'functional-coverage.production-source-manifest.json'
    DescriptorName = 'functional-coverage.native-product-descriptor.v1.json'
    BoundsName = 'functional-coverage.native-options.v1.json'
    OutputDirectory = 'native-merge'
    RegistryPath = 'scripts/Features/CodeQuality/functional-coverage.product-contributors.json'
    SettingsPath = 'scripts/Features/CodeQuality/functional-coverage.production.settings.xml'
    ToolProject = 'tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj'
    GlobalPackagesPrefix = 'global-packages: '
    ToolPackageId = 'dotnet-coverage'
    ToolRelativePath = 'tools/net8.0/any/dotnet-coverage.dll'
    ToolVersionProperty = 'KeyLoadNativeCoverageToolVersion'
    PackagesRootProperty = 'NuGetPackageRoot'
    ShaPattern = '\A[0-9a-f]{64}\z'
    RevisionPattern = '\A[0-9a-f]{40}\z'
    GuidPattern = '\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z'
    ClusterFilter = '/*/*/(PartitionQueryPublicClusterTests|McpDocumentCrudParityTests|RelationalSqlClusterJoinTests|RelationalSqlClusterJoinAuthorizationTests|RelationalSqlClusterJoinBudgetTests|RelationalSqlClusterJoinCancellationTests|RelationalSqlClusterJoinReadCutTests)/*'
    UnitCensusReportPattern = '*.tunit-report.json'
    UnitCensusTrxPattern = '*.trx'
    UnitCensusCoverageName = 'coverage.coverage'
    UnitRunStatusName = 'functional-coverage.unit-run-status.v1.json'
    ReportPattern = '*.tunit-report.json'
    TrxPattern = '*.trx'
    CoverageName = 'coverage.coverage'
    JsonDepth = 32
    InitialReadBufferBytes = 4096
    MaximumRunManifestBytes = 4194304
    MinimumContentBytes = 1
    ExitSuccess = 0
    TimeSpanFormat = 'c'
    InvalidTool = 'The pinned native coverage tool could not be resolved from the built native test assembly.'
}
function Assert-DescriptorInputs {
    if (-not [IO.Path]::IsPathFullyQualified($Repository) -or
        -not [IO.Path]::IsPathFullyQualified($SourceManifestPath) -or
        -not [IO.Path]::IsPathFullyQualified($UnitRunStatusPath) -or
        -not [IO.Path]::IsPathFullyQualified($ResultsRoot)) { throw $script:FcNativeProductDescriptor.Invalid }
    $script:FcNativeMergeInput.MaximumJsonDepth = $script:FcNativeProductDescriptor.JsonDepth
    $script:FcNativeMergeInput.ReadBufferBytes = $script:FcNativeProductDescriptor.InitialReadBufferBytes
    $script:FcNativeMergeInput.MaximumFileBytes = $script:FcNativeProductDescriptor.MaximumRunManifestBytes
    $script:FcNativeMergeInput.MaximumManifestBytes = $script:FcNativeProductDescriptor.MaximumRunManifestBytes
    $script:FcNativeMergeInput.MaximumReportBytes = $script:FcNativeProductDescriptor.MaximumRunManifestBytes
    $script:FcNativeMergeInput.ReferenceCatalog = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $script:FcNativeMergeInput.ReferenceBytes = 0L
    $script:FcNativeProductDescriptor.RepositoryRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Repository))
    $script:FcNativeProductDescriptor.ResultsRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($ResultsRoot))
    $script:FcNativeProductDescriptor.SourceManifestPath = [IO.Path]::GetFullPath($SourceManifestPath)
    $script:FcNativeProductDescriptor.UnitRunStatusPath = [IO.Path]::GetFullPath($UnitRunStatusPath)
    Assert-FcNativeNoReparsePath $script:FcNativeProductDescriptor.RepositoryRoot
    Assert-FcNativeNoReparsePath $script:FcNativeProductDescriptor.ResultsRoot
    $relativeResults = [IO.Path]::GetRelativePath($script:FcNativeProductDescriptor.RepositoryRoot,
        $script:FcNativeProductDescriptor.ResultsRoot)
    $relativeSource = [IO.Path]::GetRelativePath($script:FcNativeProductDescriptor.ResultsRoot,
        $script:FcNativeProductDescriptor.SourceManifestPath)
    $relativeRunStatus = [IO.Path]::GetRelativePath($script:FcNativeProductDescriptor.ResultsRoot,
        [IO.Path]::GetFullPath($UnitRunStatusPath))
    if (-not [IO.Directory]::Exists($script:FcNativeProductDescriptor.ResultsRoot) -or
        [IO.Path]::IsPathRooted($relativeResults) -or $relativeResults -eq '..' -or
        $relativeResults.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relativeSource) -or $relativeSource -eq '..' -or
        $relativeSource.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relativeRunStatus) -or
        $relativeRunStatus.Replace([IO.Path]::DirectorySeparatorChar, '/') -cne $script:FcNativeProductDescriptor.UnitRunStatusName) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    $Repository = $script:FcNativeProductDescriptor.RepositoryRoot
    $ResultsRoot = $script:FcNativeProductDescriptor.ResultsRoot
    $SourceManifestPath = $script:FcNativeProductDescriptor.SourceManifestPath
    $UnitRunStatusPath = $script:FcNativeProductDescriptor.UnitRunStatusPath
}
function Get-PolicyBounds([string] $Root) {
    # This is an admission/tooling default snapshot, never observed host or workload configuration.
    $snapshot = Get-FcTestIdentitySnapshot $Root
    $assemblyPath = Resolve-FcPath $Root $snapshot.compiledIdentity.dll
    $assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
    $type = $assembly.GetType('KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageExecutionOptions', $true)
    $options = [Activator]::CreateInstance($type, $true)
    $validation = $type.GetMethod('IsValid', [Reflection.BindingFlags] 'Instance,NonPublic')
    if ($null -eq $validation -or -not $validation.Invoke($options, @())) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    $bounds = [ordered]@{}
    foreach ($name in @('MaximumDescriptorBytes','MaximumFiles','ReadBufferBytes','MaximumTotalBytes',
        'MaximumFileBytes','MaximumPathCharacters','MaximumManifestBytes','MaximumReportBytes')) {
        $value = $type.GetProperty($name).GetValue($options)
        if ($value -le 0) { throw $script:FcNativeProductDescriptor.Invalid }
        $bounds[$name.Substring(0,1).ToLowerInvariant() + $name.Substring(1)] = $value
    }
    foreach ($name in @('ShutdownTimeout','SettlementTimeout','ProcessStopTimeout','ApplicationCleanupTimeout')) {
        $duration = [TimeSpan] $type.GetProperty($name).GetValue($options)
        if ($duration.Ticks -le 0 -or $duration.Ticks % [TimeSpan]::TicksPerSecond -ne 0) {
            throw $script:FcNativeProductDescriptor.Invalid
        }
        $key = $name.Substring(0,1).ToLowerInvariant() + $name.Substring(1) + 'Seconds'
        $bounds[$key] = [long] $duration.TotalSeconds
    }
    foreach ($name in @('ReadBufferBytes','MaximumFiles','MaximumTotalBytes','MaximumFileBytes',
        'MaximumManifestBytes','MaximumReportBytes','MaximumPathCharacters','SettlementTimeoutSeconds')) {
        $script:FcNativeMergeInput[$name] = $bounds[$name.Substring(0,1).ToLowerInvariant() + $name.Substring(1)]
    }
    [ordered]@{ bounds = $bounds; testIdentity = $snapshot.compiledIdentity }
}

function Assert-ExactKeys([Collections.IDictionary] $Value, [string[]] $Names) {
    if ($null -eq $Value -or $Value.Count -ne $Names.Count) { throw $script:FcNativeProductDescriptor.Invalid }
    foreach ($name in $Names) { if (-not $Value.Contains($name)) { throw $script:FcNativeProductDescriptor.Invalid } }
}

function Read-BoundedJson([string] $Path, [long] $MaximumBytes) {
    $full = [IO.Path]::GetFullPath($Path)
    Assert-FcNativeNoReparsePath $full
    $file = Read-FcNativeBoundedFile $full $MaximumBytes $script:FcNativeMergeInput.ReadBufferBytes `
        $script:FcNativeProductDescriptor.Invalid
    $json = $null
    try {
        $json = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($file.bytes))
        Assert-FcNativeJsonUnique $json.RootElement
        $value = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($file.bytes)) -AsHashtable `
            -Depth $script:FcNativeProductDescriptor.JsonDepth
        [ordered]@{ path = $full; value = $value; bytes = $file.bytes; length = $file.length; sha256 = $file.sha256 }
    }
    finally { if ($null -ne $json) { $json.Dispose() } }
}

function New-EvidenceReference([string] $Path, [long] $MaximumBytes) {
    $full = [IO.Path]::GetFullPath($Path)
    $relative = [IO.Path]::GetRelativePath($script:FcNativeProductDescriptor.ResultsRoot, $full).Replace([IO.Path]::DirectorySeparatorChar, '/')
    $resolved = Resolve-FcNativeEvidencePath $script:FcNativeProductDescriptor.ResultsRoot $relative `
        $script:FcNativeMergeInput.MaximumPathCharacters
    $read = Read-FcNativeHashFile $resolved $MaximumBytes $script:FcNativeMergeInput.ReadBufferBytes `
        $script:FcNativeProductDescriptor.Invalid
    [ordered]@{ path = $relative; length = $read.length; sha256 = $read.sha256 }
}

function Get-UniqueReport([string] $Directory, [string] $Pattern) {
    Assert-FcNativeNoReparsePath $Directory
    $match = $null
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Filter $Pattern -File -Force) {
        if ($null -ne $match) { throw $script:FcNativeProductDescriptor.Invalid }
        $match = $item
    }
    if ($null -eq $match) { throw $script:FcNativeProductDescriptor.Invalid }
    $match.FullName
}

function Assert-CompleteContributorModules([object[]] $Rows, [object] $Manifest) {
    $required = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($product in $Manifest.compiledProducts) {
        if ($product.role -ceq 'production') { [void]$required.Add([string]$product.module) }
    }
    if ($required.Count -eq 0) { throw $script:FcNativeProductDescriptor.Invalid }
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($row in $Rows) { foreach ($module in $row.executedModules) { [void]$observed.Add([string]$module) } }
    if (-not $observed.SetEquals($required)) {
        $missing = @($required | Where-Object { -not $observed.Contains($_) } | Sort-Object)
        [Console]::Error.WriteLine('Product coverage contributor modules are not admitted: ' + ($missing -join ', '))
        throw $script:FcNativeProductDescriptor.Invalid
    }
}

function Read-ToolProperty([string] $Root, [string] $Property, [object] $Bounds) {
    $project = Join-Path $Root $script:FcNativeProductDescriptor.ToolProject
    $arguments = [string[]] @('msbuild', $project, ('-getProperty:' + $Property), '-nologo')
    $result = Invoke-FcCoverageProcess 'dotnet' $arguments ([int]$Bounds.applicationCleanupTimeoutSeconds) `
        ([int]$Bounds.maximumManifestBytes)
    if ($result.failures.Count -gt 0 -or $result.exitCode -ne $script:FcNativeProductDescriptor.ExitSuccess -or
        -not $result.exitJoined -or -not $result.outputJoined -or -not $result.errorJoined -or -not $result.disposed -or
        -not [string]::IsNullOrWhiteSpace([string]$result.stderr)) {
        throw $script:FcNativeProductDescriptor.InvalidTool
    }
    $value = ([string]$result.stdout).TrimEnd("`r", "`n")
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Contains("`n") -or $value.Contains("`r")) {
        throw $script:FcNativeProductDescriptor.InvalidTool
    }
    $value
}

function Get-ToolMetadata([string] $Root, [object] $Bounds) {
    $version = Read-ToolProperty $Root $script:FcNativeProductDescriptor.ToolVersionProperty $Bounds
    $packageRootValue = Read-ToolProperty $Root $script:FcNativeProductDescriptor.PackagesRootProperty $Bounds
    $packageRoot = [IO.Path]::GetFullPath((Join-Path $packageRootValue `
        (Join-Path $script:FcNativeProductDescriptor.ToolPackageId $version)))
    $toolBinary = Join-Path $packageRoot $script:FcNativeProductDescriptor.ToolRelativePath
    if (-not [IO.File]::Exists($toolBinary)) { throw $script:FcNativeProductDescriptor.InvalidTool }
    $script:FcNativeMergeInput.ReadBufferBytes = [int]$Bounds.readBufferBytes
    $script:FcNativeMergeInput.MaximumFiles = [int]$Bounds.maximumFiles
    $script:FcNativeMergeInput.MaximumTotalBytes = [long]$Bounds.maximumTotalBytes
    $script:FcNativeMergeInput.MaximumFileBytes = [long]$Bounds.maximumFileBytes
    $script:FcNativeMergeInput.SettlementTimeoutSeconds = [int]$Bounds.settlementTimeoutSeconds
    $closure = Read-FcNativeToolClosure $packageRoot $version $Bounds $null
    [ordered]@{ packageId = $script:FcNativeProductDescriptor.ToolPackageId; version = $version
        packageRoot = $packageRoot; nupkgSha512 = $closure.nupkgSha512; closureDigest = $closure.closureDigest }
}

function Get-SuiteRun([string] $Suite, [int] $ExitCode, [object] $Source, [object] $ImageBySuite,
    [object] $Bounds, [string] $ResultsRoot, [string] $Filter = '', [object] $Status = $null) {
    if ($ExitCode -ne $script:FcNativeProductDescriptor.ExitSuccess) {
        throw "The native $Suite coverage child did not complete successfully; raw outputs are retained."
    }
    $directoryName = if ($null -ne $Status) { [string] $Status.resultsDirectory } else { $Suite }
    $directory = if ($Suite -ceq 'cluster') { $ResultsRoot } else { Join-Path $ResultsRoot $directoryName }
    $report = Get-UniqueReport $directory $script:FcNativeProductDescriptor.ReportPattern
    $trx = Get-UniqueReport $directory $script:FcNativeProductDescriptor.TrxPattern
    $coverage = Join-Path $directory $script:FcNativeProductDescriptor.CoverageName
    $native = Read-BoundedJson $report $Bounds.maximumReportBytes
    $runId = [Guid]::ParseExact([string] $native.value.publicationGeneration, 'N').ToString('D').ToLowerInvariant()
    $baseSuite = Get-FcNativeBaseSuite $Suite
    $run = [ordered]@{ suite = $Suite; runId = $runId; sourceRevision = $Source.value.sourceRevision
        sourceManifestSha256 = $Source.file.sha256; testImageManifestSha256 = $ImageBySuite[$baseSuite].sha256
        nativeExitCode = $ExitCode; functionalReport = New-EvidenceReference $report $Bounds.maximumReportBytes
        trx = New-EvidenceReference $trx $Bounds.maximumReportBytes
        coverage = New-EvidenceReference $coverage $Bounds.maximumReportBytes }
    if ($null -ne $Status) {
        $run.statusId = [string] $Status.id
        $run.filter = $Filter
        $run.coverageEnabled = $true
        $run.resultsDirectory = $directoryName
    }
    $run
}

function Get-UnitCensusRun([string] $StatusId, [object] $Status, [object] $Source,
    [object] $ImageBySuite, [object] $Bounds, [string] $ResultsRoot) {
    $suite = [string] $Status.suite
    if ($Status.id -cne $StatusId -or $Status.exitCode -ne $script:FcNativeProductDescriptor.ExitSuccess -or
        $Status.coverageEnabled -ne $false -or $Status.suite -cne $suite -or $Status.filter -cne '' -or
        $Status.resultsDirectory -cne $StatusId) {
        throw "The full uninstrumented $suite census did not complete successfully."
    }
    $directoryName = [string] $Status.resultsDirectory
    $directory = Join-Path $ResultsRoot $directoryName
    $report = Get-UniqueReport $directory $script:FcNativeProductDescriptor.UnitCensusReportPattern
    $trx = Get-UniqueReport $directory $script:FcNativeProductDescriptor.UnitCensusTrxPattern
    if ([IO.File]::Exists((Join-Path $directory $script:FcNativeProductDescriptor.UnitCensusCoverageName))) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    [ordered]@{ statusId = $StatusId; suite = $suite; runId = $StatusId; filter = ''
        coverageEnabled = $false; resultsDirectory = $directoryName
        sourceRevision = $Source.value.sourceRevision; sourceManifestSha256 = $Source.file.sha256
        testImageManifestSha256 = $ImageBySuite[$suite].sha256; nativeExitCode = $Status.exitCode
        functionalReport = New-EvidenceReference $report $Bounds.maximumReportBytes
        trx = New-EvidenceReference $trx $Bounds.maximumReportBytes }
}

function Get-UnitGroupSuites {
    $suites = [Collections.Generic.List[string]]::new()
    foreach ($prefix in @($script:FcNativeContributors.UnitGroupPrefix)) {
        for ($index = 1; $index -le $script:FcNativeContributors.UnitGroupCount; $index++) {
            $suites.Add($prefix + $index.ToString($script:FcNativeContributors.UnitGroupIdFormat,
                [Globalization.CultureInfo]::InvariantCulture))
        }
    }
    $suites.ToArray()
}

function Get-ClusterLifecycles([string] $Root, [string] $Results, [object] $Bounds) {
    $observed = Get-FcNativeProcessLifecycleEvidence $Root $Results $SourceManifestPath
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($original in $observed.originals) {
        if ([IO.Path]::GetFileName($original.path) -cne 'native-process-startup.json') { continue }
        $directory = [IO.Path]::GetDirectoryName((Resolve-FcPath $Root $original.path))
        $rows.Add([ordered]@{
            startup = New-EvidenceReference (Join-Path $directory 'native-process-startup.json') $Bounds.maximumManifestBytes
            beforeShutdown = New-EvidenceReference (Join-Path $directory 'native-process-before-shutdown.json') $Bounds.maximumManifestBytes
            shutdown = New-EvidenceReference (Join-Path $directory 'native-process-shutdown.json') $Bounds.maximumManifestBytes
        })
    }
    if ($rows.Count -eq 0 -or $rows.Count -gt $Bounds.maximumFiles) { throw $script:FcNativeProductDescriptor.Invalid }
    $rows.ToArray()
}

function Write-CreateOnly([string] $Path, [byte[]] $Bytes) {
    if ([IO.File]::Exists($Path) -or [IO.Directory]::Exists($Path)) { throw $script:FcNativeProductDescriptor.Invalid }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}

function New-ProductRunEvidence([string] $Root, [string] $Results, [object] $Bounds, [object] $Source,
    [object] $ImageBySuite, [string] $StatusPath, [object] $StatusRead, [object] $StatusRuns,
    [int] $RecoveryExitCode, [int] $ClusterExitCode) {
    $groupSuites = [string[]] @(Get-UnitGroupSuites)
    $census = [ordered]@{
        normal = Get-UnitCensusRun 'unit-census' $statusRuns['unit-census'] $source $imageBySuite $bounds $results
    }
    $codes = @{ recovery = $RecoveryExitCode; cluster = $ClusterExitCode }
    $runs = [Collections.Generic.List[object]]::new()
    foreach ($suite in $groupSuites + @('recovery','cluster')) {
        $statusRow = if ($statusRuns.ContainsKey($suite)) { $statusRuns[$suite] } else { $null }
        $exitCode = if ($null -ne $statusRow) { [int] $statusRow.exitCode } else { $codes[$suite] }
        $filter = if ($null -ne $statusRow) { [string] $statusRow.filter } else { '' }
        $runs.Add((Get-SuiteRun $suite $exitCode $source $imageBySuite $bounds $results $filter $statusRow))
    }
    $lifecycles = @(Get-ClusterLifecycles $root $results $bounds)
    $tool = Get-ToolMetadata $root $bounds
    $statusReference = New-EvidenceReference $statusPath $script:FcNativeContributors.UnitRunStatusMaximumBytes
    if ($statusReference.length -ne $statusRead.length -or $statusReference.sha256 -cne $statusRead.sha256) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    [ordered]@{ census = $census; runs = @($runs.ToArray()); lifecycles = $lifecycles; tool = $tool
        statusReference = $statusReference }
}

function Invoke-DescriptorProducer {
    Assert-DescriptorInputs
    $root = $script:FcNativeProductDescriptor.RepositoryRoot
    $results = $script:FcNativeProductDescriptor.ResultsRoot
    $policy = Get-PolicyBounds $root
    $bounds = $policy.bounds
    $statusPath = $script:FcNativeProductDescriptor.UnitRunStatusPath
    $statusRead = Read-BoundedJson $statusPath $script:FcNativeContributors.UnitRunStatusMaximumBytes
    $sourceRef = New-EvidenceReference $SourceManifestPath $bounds.maximumManifestBytes
    $source = Read-FcNativeJsonReference $results $sourceRef $bounds.maximumManifestBytes $bounds.maximumPathCharacters
    Assert-FcNativeSourceManifest $root $source.value
    if ($source.value.sourceRevision -cnotmatch $script:FcNativeProductDescriptor.RevisionPattern -or
        $source.value.sourceRevision -cne $env:GITHUB_SHA) { throw $script:FcNativeProductDescriptor.Invalid }
    $contributors = Read-FcNativeContributors $source.value $bounds ([string[]]$script:FcNativeMergeInput.ModuleRoster)
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($suite in $script:FcNativeContributors.Suites) {
        foreach ($row in $contributors[$suite]) { $rows.Add($row) }
    }
    Assert-CompleteContributorModules @($rows.ToArray()) $source.value
    $imageBySuite = Read-FcNativeTestImageManifests $results $root $source.value $bounds
    if ($imageBySuite.Count -ne 3) { throw $script:FcNativeProductDescriptor.Invalid }
    $inventory = Read-FcNativeUnitInventory $root $source.value $bounds $imageBySuite $contributors
    $statusRuns = Assert-FcNativeUnitRunStatus $statusRead.value $inventory.value $source.value.sourceRevision `
        $source.file.sha256
    if ($policy.testIdentity.dllSha256 -cne $imageBySuite['unit'].identity.compiledIdentity.dllSha256 -or
        $policy.testIdentity.pdbSha256 -cne $imageBySuite['unit'].identity.compiledIdentity.pdbSha256) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    $runEvidence = New-ProductRunEvidence $root $results $bounds $source $imageBySuite `
        $statusPath $statusRead $statusRuns $RecoveryExitCode $ClusterExitCode
    $descriptor = [ordered]@{ schemaVersion = 3; invocationId = [Guid]::NewGuid().ToString('D').ToLowerInvariant()
        evidenceRoot = $results; sourceManifest = $sourceRef; unitRunStatus = $runEvidence.statusReference
        tool = $runEvidence.tool; bounds = $bounds; unitCensus = $runEvidence.census
        suiteRuns = $runEvidence.runs; clusterLifecycles = @($runEvidence.lifecycles)
        outputDirectory = $script:FcNativeProductDescriptor.OutputDirectory }
    $descriptorPath = Join-Path $results 'functional-coverage.native-product-descriptor.v1.json'
    $nativeOptionsPath = Join-Path $results 'functional-coverage.native-options.v1.json'
    $descriptorBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $descriptor -Depth 32) + "`n")
    $boundsBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $bounds -Depth 8 -Compress) + "`n")
    if ($descriptorBytes.Length -gt $bounds.maximumDescriptorBytes -or $boundsBytes.Length -gt $bounds.maximumDescriptorBytes) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    Write-CreateOnly $nativeOptionsPath $boundsBytes
    Write-CreateOnly $descriptorPath $descriptorBytes
    [Console]::WriteLine($descriptorPath)
}

Invoke-DescriptorProducer
