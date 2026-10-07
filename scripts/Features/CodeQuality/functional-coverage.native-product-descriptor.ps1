[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Repository,
    [Parameter(Mandatory = $true)][string] $SourceManifestPath,
    [Parameter(Mandatory = $true)][string] $ResultsRoot,
    [Parameter(Mandatory = $true)][string] $UnitRunStatusPath,
    [Parameter(Mandatory = $true)][int] $RecoveryExitCode,
    [Parameter(Mandatory = $true)][int] $Rf3ExitCode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'functional-coverage.shared.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compiled-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compile-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.test-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.inputs.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.files.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.process.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.test-images.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.contributors.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.unit-inventory.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.native-merge.unit-selectors.ps1')

$script:FcNativeProductDescriptor = [ordered]@{
    Invalid = 'The original functional coverage cohort is incomplete or mismatched.'
    SourceName = 'functional-coverage.production-source-manifest.json'
    RunName = 'functional-coverage.rf3-run.v1.json'
    FixtureName = 'functional-coverage.rf3-fixture.v1.json'
    DescriptorName = 'functional-coverage.native-product-descriptor.v1.json'
    BoundsName = 'functional-coverage.native-options.v1.json'
    OutputDirectory = 'native-merge'
    RegistryPath = 'scripts/Features/CodeQuality/functional-coverage.product-contributors.json'
    SettingsPath = 'scripts/Features/CodeQuality/functional-coverage.production.settings.xml'
    AppHostProject = 'src/KeyLoad.AppHost/KeyLoad.AppHost.csproj'
    GlobalPackagesPrefix = 'global-packages: '
    ToolPackageId = 'dotnet-coverage'
    ToolRelativePath = 'tools/net8.0/any/dotnet-coverage.dll'
    ToolVersionProperty = 'KeyLoadNativeCoverageToolVersion'
    PackagesRootProperty = 'NuGetPackageRoot'
    ShaPattern = '\A[0-9a-f]{64}\z'
    RevisionPattern = '\A[0-9a-f]{40}\z'
    GuidPattern = '\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z'
    Rf3Filter = '/*/*/(PartitionQueryPublicRf3Tests|McpDocumentCrudParityTests|RelationalSqlRf3JoinTests|RelationalSqlRf3JoinAuthorizationTests|RelationalSqlRf3JoinBudgetTests|RelationalSqlRf3JoinCancellationTests|RelationalSqlRf3JoinReadCutTests)/*'
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
    InvalidTool = 'The pinned native coverage tool could not be resolved from the built AppHost.'
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
function Get-PolicyBounds([string] $RunManifestPath) {
    $file = Read-BoundedJson $RunManifestPath $script:FcNativeProductDescriptor.MaximumRunManifestBytes
    $run = $file.value
    if ($run.schemaVersion -ne 1 -or $run.suite -cne 'rf3' -or $run.filter -cne $script:FcNativeProductDescriptor.Rf3Filter -or
        $run.runId -cnotmatch $script:FcNativeProductDescriptor.GuidPattern -or
        $run.sourceRevision -cnotmatch $script:FcNativeProductDescriptor.RevisionPattern -or
        $run.executionPolicy -isnot [Collections.IDictionary] -or
        $run.executionPolicy.coverage -isnot [Collections.IDictionary]) { throw $script:FcNativeProductDescriptor.Invalid }
    $coverage = $run.executionPolicy.coverage
    $expected = @('maximumDescriptorBytes','maximumFiles','readBufferBytes','maximumTotalBytes','maximumFileBytes',
        'maximumPathCharacters','maximumManifestBytes','maximumReportBytes','shutdownTimeout','settlementTimeout',
        'containerStopTimeout','applicationCleanupTimeout')
    Assert-ExactKeys $coverage $expected
    $bounds = [ordered]@{}
    foreach ($name in $expected[0..7]) {
        $value = $coverage[$name]
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -le 0) {
            throw $script:FcNativeProductDescriptor.Invalid
        }
        $bounds[$name] = $value
    }
    foreach ($name in @('shutdownTimeout','settlementTimeout','containerStopTimeout','applicationCleanupTimeout')) {
        $duration = [TimeSpan]::ParseExact([string]$coverage[$name], $script:FcNativeProductDescriptor.TimeSpanFormat,
            [Globalization.CultureInfo]::InvariantCulture)
        if ($duration.Ticks -le 0 -or $duration.Ticks % [TimeSpan]::TicksPerSecond -ne 0) {
            throw $script:FcNativeProductDescriptor.Invalid
        }
        $bounds[$name + 'Seconds'] = [long]$duration.TotalSeconds
    }
    $script:FcNativeMergeInput.ReadBufferBytes = [int]$bounds.readBufferBytes
    $script:FcNativeMergeInput.MaximumFiles = [int]$bounds.maximumFiles
    $script:FcNativeMergeInput.MaximumTotalBytes = [long]$bounds.maximumTotalBytes
    $script:FcNativeMergeInput.MaximumFileBytes = [long]$bounds.maximumFileBytes
    $script:FcNativeMergeInput.MaximumManifestBytes = [long]$bounds.maximumManifestBytes
    $script:FcNativeMergeInput.MaximumReportBytes = [long]$bounds.maximumReportBytes
    $script:FcNativeMergeInput.MaximumPathCharacters = [int]$bounds.maximumPathCharacters
    $run.file = $file
    [ordered]@{ run = $run; bounds = $bounds }
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
    if ($required.Count -ne 14) { throw $script:FcNativeProductDescriptor.Invalid }
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($row in $Rows) { foreach ($module in $row.executedModules) { [void]$observed.Add([string]$module) } }
    if (-not $observed.SetEquals($required)) {
        $missing = @($required | Where-Object { -not $observed.Contains($_) } | Sort-Object)
        [Console]::Error.WriteLine('Product coverage contributor modules are not admitted: ' + ($missing -join ', '))
        throw $script:FcNativeProductDescriptor.Invalid
    }
}

function Read-AppHostProperty([string] $Root, [string] $Property, [object] $Bounds) {
    $project = Join-Path $Root $script:FcNativeProductDescriptor.AppHostProject
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

function Get-AppHostToolMetadata([string] $Root, [object] $Bounds) {
    $version = Read-AppHostProperty $Root $script:FcNativeProductDescriptor.ToolVersionProperty $Bounds
    $packageRootValue = Read-AppHostProperty $Root $script:FcNativeProductDescriptor.PackagesRootProperty $Bounds
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
    [object] $Bounds, [string] $ResultsRoot, [string] $Rf3RunId, [string] $Filter = '', [object] $Status = $null) {
    if ($ExitCode -ne $script:FcNativeProductDescriptor.ExitSuccess) {
        throw "The native $Suite coverage child did not complete successfully; raw outputs are retained."
    }
    $directoryName = if ($null -ne $Status) { [string] $Status.resultsDirectory } else { $Suite }
    $directory = if ($Suite -ceq 'rf3') { $ResultsRoot } else { Join-Path $ResultsRoot $directoryName }
    $report = Get-UniqueReport $directory $script:FcNativeProductDescriptor.ReportPattern
    $trx = Get-UniqueReport $directory $script:FcNativeProductDescriptor.TrxPattern
    $coverage = Join-Path $directory $script:FcNativeProductDescriptor.CoverageName
    $runId = if ($Suite -ceq 'rf3') { $Rf3RunId } else { [Guid]::NewGuid().ToString('D').ToLowerInvariant() }
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
    foreach ($prefix in @($script:FcNativeContributors.UnitGroupPrefix, $script:FcNativeContributors.ScalarUnitGroupPrefix)) {
        for ($index = 1; $index -le $script:FcNativeContributors.UnitGroupCount; $index++) {
            $suites.Add($prefix + $index.ToString($script:FcNativeContributors.UnitGroupIdFormat,
                [Globalization.CultureInfo]::InvariantCulture))
        }
    }
    $suites.ToArray()
}

function Get-Rf3Fixture([string] $ResultsRoot, [object] $Run, [object] $Source, [object] $Bounds) {
    $fixtureRead = Read-BoundedJson (Join-Path $ResultsRoot $script:FcNativeProductDescriptor.FixtureName) `
        $Bounds.maximumManifestBytes
    $fixture = $fixtureRead.value
    if ($fixture.schemaVersion -ne 1 -or $fixture.functionalRunId -cne $Run.runId -or
        $fixture.sourceRevision -cne $Source.value.sourceRevision -or
        $fixture.sourceManifestSha256 -cne $Source.file.sha256 -or $fixture.suite -cne 'rf3' -or
        $fixture.nodes -isnot [array] -or $fixture.nodes.Count -ne 3 -or $fixture.caseIdentities -isnot [array]) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    $nodes = [Collections.Generic.List[object]]::new()
    foreach ($node in $fixture.nodes) {
        Assert-ExactKeys $node @('node','containerId','imageId','session','serverPid','serverStartTicks','terminal','coverage','contextManifest')
        $nodes.Add([ordered]@{ node = $node.node; session = $node.session; coverage = $node.coverage
            terminal = $node.terminal; contextManifest = $node.contextManifest })
    }
    $caseStrings = [Collections.Generic.List[string]]::new()
    foreach ($case in $fixture.caseIdentities) {
        Assert-ExactKeys $case @('className','methodName','instanceName')
        $caseStrings.Add(([string]$case.className) + '|' + ([string]$case.methodName) + '|' + ([string]$case.instanceName))
    }
    [ordered]@{ fixtureId = $fixture.fixtureId; sourceRevision = $Source.value.sourceRevision
        sourceManifestSha256 = $Source.file.sha256; functionalRunId = $Run.runId; caseIdentities = @($caseStrings.ToArray())
        nodes = @($nodes.ToArray()); fixtureReceipt = New-EvidenceReference $fixtureRead.path $Bounds.maximumManifestBytes }
}

function Write-CreateOnly([string] $Path, [byte[]] $Bytes) {
    if ([IO.File]::Exists($Path) -or [IO.Directory]::Exists($Path)) { throw $script:FcNativeProductDescriptor.Invalid }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}

function New-ProductRunEvidence([string] $Root, [string] $Results, [object] $Bounds, [object] $Policy, [object] $Source,
    [object] $ImageBySuite, [string] $StatusPath, [object] $StatusRead, [object] $StatusRuns,
    [int] $RecoveryExitCode, [int] $Rf3ExitCode) {
    $groupSuites = [string[]] @(Get-UnitGroupSuites)
    $census = [ordered]@{
        normal = Get-UnitCensusRun 'unit-census' $statusRuns['unit-census'] $source $imageBySuite $bounds $results
        scalar = Get-UnitCensusRun 'unit-scalar-census' $statusRuns['unit-scalar-census'] $source $imageBySuite $bounds $results
    }
    $codes = @{ recovery = $RecoveryExitCode; rf3 = $Rf3ExitCode }
    $runs = [Collections.Generic.List[object]]::new()
    foreach ($suite in $groupSuites + @('recovery','rf3')) {
        $statusRow = if ($statusRuns.ContainsKey($suite)) { $statusRuns[$suite] } else { $null }
        $exitCode = if ($null -ne $statusRow) { [int] $statusRow.exitCode } else { $codes[$suite] }
        $runId = if ($suite -ceq 'rf3') { [string]$policy.run.runId } else { $null }
        $filter = if ($null -ne $statusRow) { [string] $statusRow.filter } else { '' }
        $runs.Add((Get-SuiteRun $suite $exitCode $source $imageBySuite $bounds $results $runId $filter $statusRow))
    }
    $rf3Run = @($runs.ToArray() | Where-Object suite -CEQ 'rf3')[0]
    $fixture = Get-Rf3Fixture $results $rf3Run $source $bounds
    $tool = Get-AppHostToolMetadata $root $bounds
    $statusReference = New-EvidenceReference $statusPath $script:FcNativeContributors.UnitRunStatusMaximumBytes
    if ($statusReference.length -ne $statusRead.length -or $statusReference.sha256 -cne $statusRead.sha256) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    [ordered]@{ census = $census; runs = @($runs.ToArray()); fixture = $fixture; tool = $tool
        statusReference = $statusReference }
}

function Invoke-DescriptorProducer {
    Assert-DescriptorInputs
    $root = $script:FcNativeProductDescriptor.RepositoryRoot
    $results = $script:FcNativeProductDescriptor.ResultsRoot
    $runPath = Join-Path $results $script:FcNativeProductDescriptor.RunName
    $policy = Get-PolicyBounds $runPath
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
    if ($imageBySuite.Count -ne 4) { throw $script:FcNativeProductDescriptor.Invalid }
    $inventory = Read-FcNativeUnitInventory $root $source.value $bounds $imageBySuite $contributors
    $statusRuns = Assert-FcNativeUnitRunStatus $statusRead.value $inventory.value $policy.run.sourceRevision `
        $policy.run.sourceManifestSha256
    if ($policy.run.sourceRevision -cne $source.value.sourceRevision -or
        $policy.run.sourceManifestSha256 -cne $source.file.sha256 -or
        $policy.run.testImageManifestSha256 -cne $imageBySuite['rf3'].sha256) {
        throw $script:FcNativeProductDescriptor.Invalid
    }
    $runEvidence = New-ProductRunEvidence $root $results $bounds $policy $source $imageBySuite `
        $statusPath $statusRead $statusRuns $RecoveryExitCode $Rf3ExitCode
    $descriptor = [ordered]@{ schemaVersion = 3; invocationId = [Guid]::NewGuid().ToString('D').ToLowerInvariant()
        evidenceRoot = $results; sourceManifest = $sourceRef; unitRunStatus = $runEvidence.statusReference
        tool = $runEvidence.tool; bounds = $bounds; unitCensus = $runEvidence.census
        suiteRuns = $runEvidence.runs; rf3Fixtures = @($runEvidence.fixture)
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
