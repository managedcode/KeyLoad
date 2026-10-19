function Assert-FcBounds([object] $Bounds, [object] $ExpectedBounds) {
    Assert-FcExactKeys $Bounds @('maximumDescriptorBytes','maximumFiles','readBufferBytes','maximumTotalBytes',
        'maximumFileBytes','maximumPathCharacters','maximumManifestBytes','maximumReportBytes',
        'shutdownTimeoutSeconds','settlementTimeoutSeconds','processStopTimeoutSeconds','applicationCleanupTimeoutSeconds')
    foreach ($name in $Bounds.Keys) {
        if ($Bounds[$name] -isnot [int] -and $Bounds[$name] -isnot [long] -or $Bounds[$name] -le 0 -or
            $ExpectedBounds[$name] -ne $Bounds[$name]) { throw $script:FcMergeInput.InvalidDescriptor }
    }
    if ($Bounds.maximumPathCharacters -gt 4096 -or $Bounds.readBufferBytes -gt $Bounds.maximumFileBytes -or
        $Bounds.maximumManifestBytes -gt $Bounds.maximumFileBytes -or $Bounds.maximumReportBytes -gt $Bounds.maximumFileBytes -or
        $Bounds.maximumFileBytes -gt $Bounds.maximumTotalBytes -or
        $Bounds.shutdownTimeoutSeconds + $Bounds.settlementTimeoutSeconds -ge $Bounds.processStopTimeoutSeconds -or
        $Bounds.applicationCleanupTimeoutSeconds -le 3 * $Bounds.processStopTimeoutSeconds) {
        throw $script:FcMergeInput.InvalidDescriptor
    }
    $script:FcMergeInput.ReadBufferBytes = [int] $Bounds.readBufferBytes
    $script:FcMergeInput.MaximumFiles = [int] $Bounds.maximumFiles
    $script:FcMergeInput.MaximumTotalBytes = [long] $Bounds.maximumTotalBytes
    $script:FcMergeInput.MaximumFileBytes = [long] $Bounds.maximumFileBytes
    $script:FcMergeInput.SettlementTimeoutSeconds = [int] $Bounds.settlementTimeoutSeconds
    $script:FcMergeInput.ReferenceCatalog = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $script:FcMergeInput.ReferenceBytes = 0L
}

function Assert-FcTool([object] $Tool, [string] $ExpectedRoot, [string] $ExpectedVersion, [object] $Bounds) {
    Assert-FcExactKeys $Tool @('packageId','version','packageRoot','nupkgSha512','closureDigest')
    if ($Tool.packageId -cne 'dotnet-coverage' -or $Tool.version -cne $ExpectedVersion -or
        $ExpectedVersion -cne '18.12.0' -or [IO.Path]::GetFullPath([string] $Tool.packageRoot) -cne [IO.Path]::GetFullPath($ExpectedRoot) -or
        $Tool.nupkgSha512 -cnotmatch '\A[A-Za-z0-9+/]{86}==$' -or $Tool.closureDigest -cnotmatch '\A[0-9a-f]{64}\z') {
        throw $script:FcMergeInput.InvalidDescriptor
    }
    $closure = Read-FcToolClosure $ExpectedRoot $ExpectedVersion $Bounds $null
    if ($closure.nupkgSha512 -cne $Tool.nupkgSha512 -or $closure.closureDigest -cne $Tool.closureDigest) {
        throw $script:FcMergeInput.InvalidEvidence
    }
}

function Read-FcProductPlan([string] $Root, [string] $Repository, [string] $Descriptor, [object] $ExpectedBounds) {
    $value = Read-FcDescriptor $Root $Descriptor $ExpectedBounds
    $manifest = Read-FcJsonReference $Root $value.sourceManifest $value.bounds.maximumManifestBytes $value.bounds.maximumPathCharacters
    Assert-FcSourceManifest $Repository $manifest.value
    Assert-FcCapturedMtpSettings $Root $manifest.file $manifest.value.settingsSha256 $value.bounds
    $images = Read-FcTestImageManifests $Root $Repository $manifest.value $value.bounds
    $contributors = Read-FcContributors $manifest.value $value.bounds ([string[]] $script:FcMergeInput.ModuleRoster)
    Assert-FcProductModuleCompleteness $contributors $manifest.value
    $inventory = Read-FcUnitInventory $Repository $manifest.value $value.bounds $images $contributors
    $statusReferencePath = [string] $value.unitRunStatus.path
    if ($statusReferencePath -cne 'functional-coverage.unit-run-status.json') {
        throw $script:FcMergeInput.InvalidDescriptor
    }
    $statusFile = Read-FcJsonReference $Root $value.unitRunStatus $script:FcContributors.UnitRunStatusMaximumBytes $value.bounds.maximumPathCharacters
    $statusRuns = Assert-FcUnitRunStatus $statusFile.value $inventory.value `
        $manifest.value.sourceRevision $manifest.file.sha256
    $census = Read-FcUnitCensus $Root $Repository $value $manifest $images $inventory.value $statusRuns
    $suiteRuns = Read-FcSuiteRuns $Root $Repository $value $manifest $images $contributors `
        $inventory.value $census $statusRuns
    $lifecycles = Read-FcClusterLifecycles $Root $value $manifest
    $allInputs = [Collections.Generic.List[object]]::new()
    foreach ($input in $suiteRuns.inputs) { $allInputs.Add($input) }
    if ($allInputs.Count -gt $value.bounds.maximumFiles) { throw $script:FcMergeInput.InvalidDescriptor }
    [ordered]@{ descriptor = $value; manifest = $manifest.value; manifestFile = $manifest.file; images = $images
        unitInventory = $inventory.value; unitCensus = $census; runtimeLifecycles = $lifecycles
        inputs = $allInputs.ToArray(); evidenceRoot = $Root }
}

function Read-FcUnitCensus([string] $Root, [string] $Repository, [object] $Value,
    [object] $Manifest, [object] $Images, [object] $Inventory, [object] $StatusRuns) {
    Assert-FcContributorExactKeys $Value.unitCensus @('normal')
    $normal = Read-FcUnitCensusRun $Root $Repository $Value $Manifest $Images $Inventory `
        $StatusRuns 'unit' 'unit-census' $Value.unitCensus.normal
    [ordered]@{ normal = $normal }
}

function Read-FcUnitCensusRun([string] $Root, [string] $Repository, [object] $Value,
    [object] $Manifest, [object] $Images, [object] $Inventory, [object] $StatusRuns,
    [string] $Suite, [string] $StatusId, [object] $Run) {
    Assert-FcContributorExactKeys $Run @('statusId','suite','runId','filter','resultsDirectory',
        'sourceRevision','sourceManifestSha256','testImageManifestSha256','runtimeExitCode','coverageEnabled',
        'functionalReport','trx')
    $status = $StatusRuns[$StatusId]
    if ($Run.statusId -cne $StatusId -or $Run.resultsDirectory -cne $status.resultsDirectory -or
        $Run.filter -cne $status.filter -or $Run.suite -cne $Suite -or $Run.runId -cne $StatusId -or
        $Run.sourceRevision -cne $Manifest.value.sourceRevision -or $Run.sourceManifestSha256 -cne $Manifest.file.sha256 -or
        $Run.testImageManifestSha256 -cne $Images[$Suite].sha256 -or
        ($Run.runtimeExitCode -isnot [int] -and $Run.runtimeExitCode -isnot [long]) -or $Run.runtimeExitCode -ne 0 -or
        $Run.coverageEnabled -isnot [bool] -or $Run.coverageEnabled -or
        $status.suite -cne $Suite -or $status.coverageEnabled -ne $false -or $status.exitCode -ne 0 -or
        $status.filter -cne $script:FcContributors.FullUnitSelector -or $status.resultsDirectory -cne $StatusId) {
        throw $script:FcMergeInput.InvalidDescriptor
    }
    Assert-FcRunEvidenceDirectory $Run $status
    $expected = @($Inventory.functionalCases) + @($Inventory.requiredNonContributorCases)
    $functional = Read-FcJsonReference $Root $Run.functionalReport $Value.bounds.maximumReportBytes $Value.bounds.maximumPathCharacters
    if ($functional.value.Contains('commitSha') -and $functional.value.commitSha -cne $Manifest.value.sourceRevision) {
        throw $script:FcMergeInput.InvalidEvidence
    }
    $runtimeIds = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $identities = @(Read-FcFunctionalCases $functional.value $expected $Suite $Repository `
        -MatchSourceLocations -ObservedIds $runtimeIds)
    $trxFile = Read-RuntimeFcEvidenceFile $Root $Run.trx $Value.bounds.maximumReportBytes $Value.bounds.maximumPathCharacters
    $trx = Read-RuntimeFcTrx $trxFile.path $Suite $expected
    $trxIdentities = @($trx.cases | ForEach-Object { [string] $_.caseIdentity } | Sort-Object)
    if ($identities.Count -ne $trxIdentities.Count -or ($identities -join "`n") -cne ($trxIdentities -join "`n")) {
        throw $script:FcMergeInput.InvalidEvidence
    }
    $functionalIdentities = @($Inventory.functionalCases | ForEach-Object {
        Get-FcTrxKey $_.className $_.methodName $_.instanceName
    } | Sort-Object)
    [ordered]@{ runId = $Run.runId; identities = $functionalIdentities; censusIdentities = $identities; runtimeIds = $runtimeIds }
}

function Assert-FcProductModuleCompleteness([object] $Contributors, [object] $Manifest) {
    $required = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($product in $Manifest.compiledProducts) {
        if ($product.role -ceq 'production') { [void] $required.Add([string] $product.module) }
    }
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($suite in $script:FcContributors.Suites) {
        foreach ($row in $Contributors[$suite]) {
            foreach ($module in $row.executedModules) { [void] $observed.Add([string] $module) }
        }
    }
    if ($required.Count -eq 0 -or -not $observed.SetEquals($required)) {
        throw $script:FcMergeInput.InvalidDescriptor
    }
}

function Read-FcDescriptor([string] $Root, [string] $Descriptor, [object] $ExpectedBounds) {
    $relative = [IO.Path]::GetRelativePath($Root, [IO.Path]::GetFullPath($Descriptor)).Replace([IO.Path]::DirectorySeparatorChar, '/')
    $canonical = Resolve-FcEvidencePath $Root $relative $ExpectedBounds.maximumPathCharacters
    $jsonFile = Read-FcBoundedFile $canonical $ExpectedBounds.maximumDescriptorBytes $ExpectedBounds.readBufferBytes $script:FcMergeInput.InvalidDescriptor
    $json = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($jsonFile.bytes))
    try { Assert-FcJsonUnique $json.RootElement }
    finally { $json.Dispose() }
    $value = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($jsonFile.bytes)) -AsHashtable -Depth 32
    Assert-FcExactKeys $value @('schemaVersion','invocationId','evidenceRoot','sourceManifest','unitRunStatus','tool','bounds',
        'unitCensus','suiteRuns','clusterLifecycles','outputDirectory')
    if (($value.schemaVersion -isnot [int] -and $value.schemaVersion -isnot [long]) -or $value.schemaVersion -ne 3 -or $value.invocationId -cnotmatch '\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z' -or
        [IO.Path]::GetFullPath([string] $value.evidenceRoot) -cne [IO.Path]::GetFullPath($Root)) { throw $script:FcMergeInput.InvalidDescriptor }
    Assert-FcBounds $value.bounds $ExpectedBounds
    Assert-FcTool $value.tool $ToolPackageRoot $ToolVersion $value.bounds
    $script:FcMergeInput.ReferenceCatalog.Add($canonical, [ordered]@{ length = $jsonFile.length; sha256 = $jsonFile.sha256 })
    $script:FcMergeInput.ReferenceBytes = $jsonFile.length
    $value
}

function Read-FcSuiteRuns([string] $Root, [string] $Repository, [object] $Value, [object] $Manifest,
    [object] $Images, [object] $Contributors, [object] $Inventory, [object] $Census, [object] $StatusRuns) {
    $expectedSuites = [Collections.Generic.List[string]]::new()
    foreach ($prefix in @($script:FcContributors.UnitGroupPrefix)) {
        for ($index = 1; $index -le $script:FcContributors.UnitGroupCount; $index++) {
            $expectedSuites.Add($prefix + $index.ToString($script:FcContributors.UnitGroupIdFormat,
                [Globalization.CultureInfo]::InvariantCulture))
        }
    }
    $expectedSuites.Add('recovery'); $expectedSuites.Add('cluster')
    if ($Value.suiteRuns -isnot [array] -or $Value.suiteRuns.Count -ne $expectedSuites.Count -or $Value.clusterLifecycles -isnot [array]) {
        throw $script:FcMergeInput.InvalidDescriptor
    }
    $seenSuites = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $caseIdentities = [Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
    $suiteIdentities = [Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
    $coveragePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $inputs = [Collections.Generic.List[object]]::new()
    foreach ($run in @($Value.suiteRuns | Sort-Object suite)) {
        $cases = Read-FcSuiteRun $Root $Repository $Value $Manifest $Images $Contributors $Inventory $Census `
            $StatusRuns $run $seenSuites $runIds $coveragePaths
        $caseIdentities.Add([string] $run.runId, $cases.identities)
        $suiteIdentities.Add([string] $run.suite, $cases.identities)
        $inputs.Add($cases.input)
    }
    if (-not $seenSuites.SetEquals([string[]] $expectedSuites.ToArray())) { throw $script:FcMergeInput.InvalidDescriptor }
    Assert-FcUnitGroupCensusUnion $suiteIdentities $Census
    [ordered]@{ inputs = $inputs.ToArray(); cases = $caseIdentities; runIds = $runIds }
}

function Assert-FcUnitGroupCensusUnion([Collections.Generic.Dictionary[string, string[]]] $SuiteIdentities,
    [object] $Census) {
    foreach ($mode in @('normal')) {
        $prefix = $script:FcContributors.UnitGroupPrefix
        $union = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        for ($index = 1; $index -le $script:FcContributors.UnitGroupCount; $index++) {
            $suite = $prefix + $index.ToString($script:FcContributors.UnitGroupIdFormat,
                [Globalization.CultureInfo]::InvariantCulture)
            foreach ($identity in $SuiteIdentities[$suite]) {
                if (-not $union.Add($identity)) { throw $script:FcMergeInput.InvalidDescriptor }
            }
        }
        $census = [Collections.Generic.HashSet[string]]::new([string[]] $Census[$mode].identities,
            [StringComparer]::Ordinal)
        if (-not $union.SetEquals($census)) { throw $script:FcMergeInput.InvalidDescriptor }
    }
}

function Read-FcSuiteRun([string] $Root, [string] $Repository, [object] $Value, [object] $Manifest,
    [object] $Images, [object] $Contributors, [object] $Inventory, [object] $Census, [object] $StatusRuns,
    [object] $Run, [Collections.Generic.HashSet[string]] $SeenSuites,
    [Collections.Generic.HashSet[string]] $RunIds, [Collections.Generic.HashSet[string]] $CoveragePaths) {
    $isUnitGroup = [string] $Run.suite -match '\Aunit-functional-[0-9]{2}\z'
    $expectedKeys = if ($isUnitGroup) { @('suite','runId','statusId','filter','coverageEnabled','resultsDirectory',
        'sourceRevision','sourceManifestSha256','testImageManifestSha256','runtimeExitCode','functionalReport','trx','coverage') } else {
        @('suite','runId','sourceRevision','sourceManifestSha256','testImageManifestSha256','runtimeExitCode',
            'functionalReport','trx','coverage')
    }
    Assert-FcExactKeys $Run $expectedKeys
    $expectedSuite = Get-FcBaseSuite ([string] $Run.suite)
    $status = if ($isUnitGroup) { $StatusRuns[[string] $Run.suite] } else { $null }
    if ([string]::IsNullOrWhiteSpace($expectedSuite) -or -not $SeenSuites.Add([string] $Run.suite) -or
        $Run.runId -cnotmatch '\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z' -or
        -not $RunIds.Add([string] $Run.runId) -or $Run.sourceRevision -cne $Manifest.value.sourceRevision -or
        $Run.sourceManifestSha256 -cne $Manifest.file.sha256 -or
        $Run.testImageManifestSha256 -cne $Images[$expectedSuite].sha256 -or
        ($isUnitGroup -and ($null -eq $status -or $Run.statusId -cne $Run.suite -or
            $Run.filter -cne $status.filter -or
            $Run.coverageEnabled -isnot [bool] -or -not $Run.coverageEnabled -or
            $Run.resultsDirectory -cne $status.resultsDirectory -or $status.suite -cne $expectedSuite -or
            $status.coverageEnabled -ne $true -or $status.exitCode -ne 0)) -or
        ($Run.runtimeExitCode -isnot [int] -and $Run.runtimeExitCode -isnot [long]) -or $Run.runtimeExitCode -ne 0) {
        throw $script:FcMergeInput.InvalidDescriptor
    }
    if ($isUnitGroup) { Assert-FcRunEvidenceDirectory $Run $status }
    $expected = Get-FcExpectedSuiteCases ([string] $Run.suite) $Contributors $Inventory
    $functional = Read-FcJsonReference $Root $Run.functionalReport $Value.bounds.maximumReportBytes $Value.bounds.maximumPathCharacters
    if ($functional.value.Contains('commitSha') -and $functional.value.commitSha -cne $Manifest.value.sourceRevision) {
        throw $script:FcMergeInput.InvalidEvidence
    }
    $requiredIds = if ($expectedSuite -ceq 'unit') { $Census.normal.runtimeIds } else { $null }
    if ($expectedSuite -ceq 'unit' -and
        $requiredIds -isnot [Collections.Generic.Dictionary[string, string]]) {
        throw $script:FcMergeInput.InvalidEvidence
    }
    $reportCases = @(Read-FcFunctionalCases $functional.value $expected ([string] $Run.suite) $Repository `
        -MatchSourceLocations:($expectedSuite -ceq 'unit') -RequiredIds $requiredIds)
    $trx = Read-FcSuiteTrx $Root $Run $expected $Value.bounds
    if ($reportCases.Count -ne $trx.identities.Count -or ($reportCases -join "`n") -cne ($trx.identities -join "`n")) {
        throw $script:FcMergeInput.InvalidEvidence
    }
    $coverage = Read-FcHashReference $Root $Run.coverage $Value.bounds.maximumReportBytes $Value.bounds.maximumPathCharacters
    if (-not $CoveragePaths.Add([string] $coverage.path)) { throw $script:FcMergeInput.InvalidDescriptor }
    [ordered]@{ identities = $reportCases; input = [ordered]@{ kind = 'suite'; suite = $Run.suite;
        runId = $Run.runId; runtimeExitCode = $Run.runtimeExitCode; path = $coverage.path;
        length = $coverage.length; sha256 = $coverage.sha256 } }
}

function Assert-FcRunEvidenceDirectory([object] $Run, [object] $Status) {
    $prefix = ([string] $Status.resultsDirectory) + '/'
    $references = [Collections.Generic.List[object]]::new()
    $references.Add($Run.functionalReport)
    $references.Add($Run.trx)
    if ($Run.Contains('coverage')) { $references.Add($Run.coverage) }
    foreach ($reference in $references) {
        if ($reference.path -isnot [string] -or -not ([string] $reference.path).StartsWith($prefix, [StringComparison]::Ordinal)) {
            throw $script:FcMergeInput.InvalidDescriptor
        }
    }
}

function Read-FcSuiteTrx([string] $Root, [object] $Run, [object[]] $Expected, [object] $Bounds) {
    $file = Read-RuntimeFcEvidenceFile $Root $Run.trx $Bounds.maximumReportBytes $Bounds.maximumPathCharacters
    $trx = Read-RuntimeFcTrx $file.path $Run.suite $Expected
    [ordered]@{ identities = @($trx.cases | ForEach-Object { [string] $_.caseIdentity } | Sort-Object) }
}

function Read-FcClusterLifecycles([string] $Root, [object] $Value, [object] $Manifest) {
    if ($Value.clusterLifecycles.Count -eq 0 -or $Value.clusterLifecycles.Count -gt $Value.bounds.maximumFiles) {
        throw $script:FcMergeInput.InvalidDescriptor
    }
    $servers = @($Manifest.value.compiledProducts | Where-Object module -CEQ 'KeyLoad')
    if ($servers.Count -ne 1) { throw $script:FcMergeInput.InvalidSource }
    $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $originals = [Collections.Generic.List[object]]::new()
    foreach ($lifecycle in $Value.clusterLifecycles) {
        Assert-FcExactKeys $lifecycle @('startup','beforeShutdown','shutdown')
        $names = [ordered]@{ startup = 'execution-process-startup.json'
            beforeShutdown = 'execution-process-before-shutdown.json'; shutdown = 'execution-process-shutdown.json' }
        $directory = $null
        foreach ($name in $names.Keys) {
            $read = Read-FcJsonReference $Root $lifecycle[$name] $Value.bounds.maximumManifestBytes $Value.bounds.maximumPathCharacters
            $parent = [IO.Path]::GetDirectoryName($read.file.path)
            if ([IO.Path]::GetFileName($read.file.path) -cne $names[$name] -or
                ($null -ne $directory -and $directory -cne $parent)) { throw $script:FcMergeInput.InvalidEvidence }
            $directory = $parent
            if ($name -ceq 'startup') {
                $nodes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($process in $read.value) {
                    if (-not $nodes.Add([string] $process.Name)) { throw $script:FcMergeInput.InvalidEvidence }
                }
                if ($read.value.Count -ne $script:FcMergeInput.ClusterNodeCount -or
                    -not $nodes.SetEquals([string[]] @('node1','node2','node3'))) { throw $script:FcMergeInput.InvalidEvidence }
            }
            $originals.Add($read.file)
        }
        if (-not $directories.Add($directory) -or
            (Assert-JoinedLifecycle $directory $servers[0]) -ne $script:FcMergeInput.ClusterNodeCount) {
            throw $script:FcMergeInput.InvalidEvidence
        }
    }
    $originals.ToArray()
}
