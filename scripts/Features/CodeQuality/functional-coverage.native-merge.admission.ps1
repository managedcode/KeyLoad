function Assert-FcNativeBounds([object] $Bounds, [object] $ExpectedBounds) {
    Assert-FcNativeExactKeys $Bounds @('maximumDescriptorBytes','maximumFiles','readBufferBytes','maximumTotalBytes',
        'maximumFileBytes','maximumPathCharacters','maximumManifestBytes','maximumReportBytes',
        'shutdownTimeoutSeconds','settlementTimeoutSeconds','containerStopTimeoutSeconds','applicationCleanupTimeoutSeconds')
    foreach ($name in $Bounds.Keys) {
        if ($Bounds[$name] -isnot [int] -and $Bounds[$name] -isnot [long] -or $Bounds[$name] -le 0 -or
            $ExpectedBounds[$name] -ne $Bounds[$name]) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    }
    if ($Bounds.maximumPathCharacters -gt 4096 -or $Bounds.readBufferBytes -gt $Bounds.maximumFileBytes -or
        $Bounds.maximumManifestBytes -gt $Bounds.maximumFileBytes -or $Bounds.maximumReportBytes -gt $Bounds.maximumFileBytes -or
        $Bounds.maximumFileBytes -gt $Bounds.maximumTotalBytes -or
        $Bounds.shutdownTimeoutSeconds + $Bounds.settlementTimeoutSeconds -ge $Bounds.containerStopTimeoutSeconds -or
        $Bounds.applicationCleanupTimeoutSeconds -le 3 * $Bounds.containerStopTimeoutSeconds) {
        throw $script:FcNativeMergeInput.InvalidDescriptor
    }
    $script:FcNativeMergeInput.ReadBufferBytes = [int] $Bounds.readBufferBytes
    $script:FcNativeMergeInput.MaximumFiles = [int] $Bounds.maximumFiles
    $script:FcNativeMergeInput.MaximumTotalBytes = [long] $Bounds.maximumTotalBytes
    $script:FcNativeMergeInput.MaximumFileBytes = [long] $Bounds.maximumFileBytes
    $script:FcNativeMergeInput.SettlementTimeoutSeconds = [int] $Bounds.settlementTimeoutSeconds
    $script:FcNativeMergeInput.ReferenceCatalog = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $script:FcNativeMergeInput.ReferenceBytes = 0L
}

function Assert-FcNativeTool([object] $Tool, [string] $ExpectedRoot, [string] $ExpectedVersion, [object] $Bounds) {
    Assert-FcNativeExactKeys $Tool @('packageId','version','packageRoot','nupkgSha512','closureDigest')
    if ($Tool.packageId -cne 'dotnet-coverage' -or $Tool.version -cne $ExpectedVersion -or
        $ExpectedVersion -cne '18.11.2' -or [IO.Path]::GetFullPath([string] $Tool.packageRoot) -cne [IO.Path]::GetFullPath($ExpectedRoot) -or
        $Tool.nupkgSha512 -cnotmatch '\A[A-Za-z0-9+/]{86}==$' -or $Tool.closureDigest -cnotmatch '\A[0-9a-f]{64}\z') {
        throw $script:FcNativeMergeInput.InvalidDescriptor
    }
    $closure = Read-FcNativeToolClosure $ExpectedRoot $ExpectedVersion $Bounds $null
    if ($closure.nupkgSha512 -cne $Tool.nupkgSha512 -or $closure.closureDigest -cne $Tool.closureDigest) {
        throw $script:FcNativeMergeInput.InvalidEvidence
    }
}

function Read-FcNativeProductPlan([string] $Root, [string] $Descriptor, [object] $ExpectedBounds) {
    $value = Read-FcNativeDescriptor $Root $Descriptor $ExpectedBounds
    $manifest = Read-FcNativeJsonReference $Root $value.sourceManifest $value.bounds.maximumManifestBytes $value.bounds.maximumPathCharacters
    Assert-FcNativeSourceManifest $Repository $manifest.value
    Assert-FcNativeCapturedMtpSettings $Root $manifest.file $manifest.value.settingsSha256 $value.bounds
    $images = Read-FcNativeTestImageManifests $Root $Repository $manifest.value $value.bounds
    $contributors = Read-FcNativeContributors $manifest.value $value.bounds ([string[]] $script:FcNativeMergeInput.ModuleRoster)
    Assert-FcNativeProductModuleCompleteness $contributors $manifest.value
    $suiteRuns = Read-FcNativeSuiteRuns $Root $value $manifest $images $contributors
    $fixtures = Read-FcNativeRf3Fixtures $Root $value $manifest $suiteRuns
    $allInputs = [Collections.Generic.List[object]]::new()
    foreach ($input in $suiteRuns.inputs) { $allInputs.Add($input) }
    foreach ($input in $fixtures.inputs) { $allInputs.Add($input) }
    if ($allInputs.Count -gt $value.bounds.maximumFiles) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    [ordered]@{ descriptor = $value; manifest = $manifest.value; manifestFile = $manifest.file; images = $images
        inputs = $allInputs.ToArray(); evidenceRoot = $Root }
}

function Assert-FcNativeProductModuleCompleteness([object] $Contributors, [object] $Manifest) {
    $required = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($product in $Manifest.compiledProducts) {
        if ($product.role -ceq 'production') { [void] $required.Add([string] $product.module) }
    }
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($suite in $script:FcNativeContributors.Suites) {
        foreach ($row in $Contributors[$suite]) {
            foreach ($module in $row.executedModules) { [void] $observed.Add([string] $module) }
        }
    }
    if ($required.Count -ne 14 -or -not $observed.SetEquals($required)) {
        throw $script:FcNativeMergeInput.InvalidDescriptor
    }
}

function Read-FcNativeDescriptor([string] $Root, [string] $Descriptor, [object] $ExpectedBounds) {
    $relative = [IO.Path]::GetRelativePath($Root, [IO.Path]::GetFullPath($Descriptor)).Replace([IO.Path]::DirectorySeparatorChar, '/')
    $canonical = Resolve-FcNativeEvidencePath $Root $relative $ExpectedBounds.maximumPathCharacters
    $jsonFile = Read-FcNativeBoundedFile $canonical $ExpectedBounds.maximumDescriptorBytes $ExpectedBounds.readBufferBytes $script:FcNativeMergeInput.InvalidDescriptor
    $json = [System.Text.Json.JsonDocument]::Parse($jsonFile.bytes)
    try { Assert-FcNativeJsonUnique $json.RootElement }
    finally { $json.Dispose() }
    $value = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($jsonFile.bytes)) -AsHashtable -Depth 32
    Assert-FcNativeExactKeys $value @('schemaVersion','invocationId','evidenceRoot','sourceManifest','tool','bounds','suiteRuns','rf3Fixtures','outputDirectory')
    if ($value.schemaVersion -ne 1 -or $value.invocationId -cnotmatch '\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z' -or
        [IO.Path]::GetFullPath([string] $value.evidenceRoot) -cne [IO.Path]::GetFullPath($Root)) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    Assert-FcNativeBounds $value.bounds $ExpectedBounds
    Assert-FcNativeTool $value.tool $ToolPackageRoot $ToolVersion $value.bounds
    $script:FcNativeMergeInput.ReferenceCatalog.Add($canonical, [ordered]@{ length = $jsonFile.length; sha256 = $jsonFile.sha256 })
    $script:FcNativeMergeInput.ReferenceBytes = $jsonFile.length
    $value
}

function Read-FcNativeSuiteRuns([string] $Root, [object] $Value, [object] $Manifest,
    [object] $Images, [object] $Contributors) {
    if ($Value.suiteRuns -isnot [array] -or $Value.suiteRuns.Count -ne 4 -or $Value.rf3Fixtures -isnot [array]) {
        throw $script:FcNativeMergeInput.InvalidDescriptor
    }
    $seenSuites = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $runIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $caseIdentities = [Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
    $coveragePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $inputs = [Collections.Generic.List[object]]::new()
    foreach ($run in @($Value.suiteRuns | Sort-Object suite)) {
        $cases = Read-FcNativeSuiteRun $Root $Value $Manifest $Images $Contributors $run $seenSuites $runIds $coveragePaths
        $caseIdentities.Add([string] $run.runId, $cases.identities)
        $inputs.Add($cases.input)
    }
    if (-not $seenSuites.SetEquals([string[]] $script:FcNativeContributors.Suites)) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    [ordered]@{ inputs = $inputs.ToArray(); cases = $caseIdentities; runIds = $runIds }
}

function Read-FcNativeSuiteRun([string] $Root, [object] $Value, [object] $Manifest,
    [object] $Images, [object] $Contributors, [object] $Run, [Collections.Generic.HashSet[string]] $SeenSuites,
    [Collections.Generic.HashSet[string]] $RunIds, [Collections.Generic.HashSet[string]] $CoveragePaths) {
    Assert-FcNativeExactKeys $Run @('suite','runId','sourceRevision','sourceManifestSha256',
        'testImageManifestSha256','nativeExitCode','functionalReport','trx','coverage')
    if ($Run.suite -cnotin $script:FcNativeContributors.Suites -or -not $SeenSuites.Add([string] $Run.suite) -or
        $Run.runId -cnotmatch '\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z' -or
        -not $RunIds.Add([string] $Run.runId) -or $Run.sourceRevision -cne $Manifest.value.sourceRevision -or
        $Run.sourceManifestSha256 -cne $Manifest.file.sha256 -or
        $Run.testImageManifestSha256 -cne $Images[[string] $Run.suite].sha256 -or
        ($Run.nativeExitCode -isnot [int] -and $Run.nativeExitCode -isnot [long]) -or $Run.nativeExitCode -ne 0) {
        throw $script:FcNativeMergeInput.InvalidDescriptor
    }
    $expected = @($Contributors[[string] $Run.suite])
    $functional = Read-FcNativeJsonReference $Root $Run.functionalReport $Value.bounds.maximumReportBytes $Value.bounds.maximumPathCharacters
    if ($functional.value.Contains('commitSha') -and $functional.value.commitSha -cne $Manifest.value.sourceRevision) {
        throw $script:FcNativeMergeInput.InvalidEvidence
    }
    $reportCases = @(Read-FcNativeFunctionalCases $functional.value $expected ([string] $Run.suite))
    $trx = Read-FcNativeSuiteTrx $Root $Run $expected $Value.bounds
    if ($reportCases.Count -ne $trx.identities.Count -or ($reportCases -join "`n") -cne ($trx.identities -join "`n")) {
        throw $script:FcNativeMergeInput.InvalidEvidence
    }
    $coverage = Read-FcNativeHashReference $Root $Run.coverage $Value.bounds.maximumReportBytes $Value.bounds.maximumPathCharacters
    if (-not $CoveragePaths.Add([string] $coverage.path)) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    [ordered]@{ identities = $reportCases; input = [ordered]@{ kind = 'suite'; suite = $Run.suite;
        runId = $Run.runId; nativeExitCode = $Run.nativeExitCode; path = $coverage.path;
        length = $coverage.length; sha256 = $coverage.sha256 } }
}

function Read-FcNativeSuiteTrx([string] $Root, [object] $Run, [object[]] $Expected, [object] $Bounds) {
    $file = Read-FcNativeEvidenceFile $Root $Run.trx $Bounds.maximumReportBytes $Bounds.maximumPathCharacters
    $trx = Read-FcNativeTrx $file.path $Run.suite $Expected
    [ordered]@{ identities = @($trx.cases | ForEach-Object { [string] $_.caseIdentity } | Sort-Object) }
}

function Read-FcNativeRf3Fixtures([string] $Root, [object] $Value, [object] $Manifest, [object] $SuiteRuns) {
    $rf3Runs = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($run in $Value.suiteRuns) { if ($run.suite -ceq 'rf3') { [void] $rf3Runs.Add([string] $run.runId) } }
    $associations = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $inputs = [Collections.Generic.List[object]]::new()
    $fixtureIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $imageIdentity = $null
    foreach ($fixture in $Value.rf3Fixtures) {
        $result = Read-FcNativeRf3Fixture $Root $Value $Manifest $SuiteRuns $fixture $rf3Runs $fixtureIds $associations $paths
        foreach ($input in $result.inputs) { $inputs.Add($input) }
        if ($null -eq $imageIdentity) { $imageIdentity = $result.imageIdentity }
        elseif ((ConvertTo-Json $imageIdentity -Compress) -cne (ConvertTo-Json $result.imageIdentity -Compress)) {
            throw $script:FcNativeMergeInput.InvalidReceipt
        }
    }
    Assert-FcNativeRf3CaseCoverage $Value $SuiteRuns $associations
    [ordered]@{ inputs = $inputs.ToArray(); imageIdentity = $imageIdentity }
}

function Read-FcNativeRf3Fixture([string] $Root, [object] $Value, [object] $Manifest,
    [object] $SuiteRuns, [object] $Fixture, [Collections.Generic.HashSet[string]] $Rf3Runs,
    [Collections.Generic.HashSet[string]] $FixtureIds, [Collections.Generic.HashSet[string]] $Associations,
    [Collections.Generic.HashSet[string]] $CoveragePaths) {
    Assert-FcNativeExactKeys $Fixture @('fixtureId','sourceRevision','sourceManifestSha256','functionalRunId',
        'caseIdentities','nodes','fixtureReceipt')
    if ($Fixture.fixtureId -cnotmatch '\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z' -or
        -not $FixtureIds.Add([string] $Fixture.fixtureId) -or $Fixture.sourceRevision -cne $Manifest.value.sourceRevision -or
        $Fixture.sourceManifestSha256 -cne $Manifest.file.sha256 -or -not $Rf3Runs.Contains([string] $Fixture.functionalRunId) -or
        $Fixture.caseIdentities -isnot [array] -or $Fixture.caseIdentities.Count -eq 0 -or
        $Fixture.nodes -isnot [array] -or $Fixture.nodes.Count -ne $script:FcNativeMergeInput.Rf3NodeCount) {
        throw $script:FcNativeMergeInput.InvalidDescriptor
    }
    $known = [Collections.Generic.HashSet[string]]::new([string[]] $SuiteRuns.cases[[string] $Fixture.functionalRunId], [StringComparer]::Ordinal)
    foreach ($identity in $Fixture.caseIdentities) {
        $association = "$($Fixture.functionalRunId)|$identity"
        if ($identity -isnot [string] -or -not $known.Contains($identity) -or -not $Associations.Add($association)) {
            throw $script:FcNativeMergeInput.InvalidDescriptor
        }
    }
    $nodeSet = Read-FcNativeRf3Nodes $Root $Value $Manifest $Fixture $CoveragePaths
    $run = @($Value.suiteRuns | Where-Object runId -ceq $Fixture.functionalRunId)[0]
    Assert-FcNativeFixtureReceipt $Root $Fixture $Manifest $run $nodeSet.nodes $Value.bounds
    [ordered]@{ inputs = $nodeSet.inputs; imageIdentity = $nodeSet.imageIdentity }
}

function Read-FcNativeRf3Nodes([string] $Root, [object] $Value, [object] $Manifest,
    [object] $Fixture, [Collections.Generic.HashSet[string]] $CoveragePaths) {
    $nodes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sessions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $inputs = [Collections.Generic.List[object]]::new()
    $nodeResults = [Collections.Generic.List[object]]::new()
    $identity = $null
    foreach ($node in $Fixture.nodes) {
        Assert-FcNativeExactKeys $node @('node','session','coverage','terminal','contextManifest')
        if ($node.node -cnotin @('node1','node2','node3') -or -not $nodes.Add([string] $node.node) -or
            $node.session -cnotmatch '\A[A-Za-z0-9._-]{1,64}\z' -or -not $sessions.Add([string] $node.session)) {
            throw $script:FcNativeMergeInput.InvalidDescriptor
        }
        $result = Read-FcNativeRf3Node $Root $Value $Manifest $Fixture $node
        if (-not $CoveragePaths.Add([string] $result.input.path)) { throw $script:FcNativeMergeInput.InvalidDescriptor }
        $inputs.Add($result.input)
        $nodeResults.Add([ordered]@{ descriptor = $node; evidence = $result })
        if ($null -eq $identity) { $identity = $result.imageIdentity }
        elseif ((ConvertTo-Json $identity -Compress) -cne (ConvertTo-Json $result.imageIdentity -Compress)) {
            throw $script:FcNativeMergeInput.InvalidReceipt
        }
    }
    if (-not $nodes.SetEquals([string[]] @('node1','node2','node3'))) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    [ordered]@{ inputs = $inputs.ToArray(); imageIdentity = $identity; nodes = $nodeResults.ToArray() }
}

function Read-FcNativeRf3Node([string] $Root, [object] $Value, [object] $Manifest, [object] $Fixture, [object] $Node) {
    $context = Read-FcNativeJsonReference $Root $Node.contextManifest $Value.bounds.maximumManifestBytes $Value.bounds.maximumPathCharacters
    $terminal = Read-FcNativeJsonReference $Root $Node.terminal $Value.bounds.maximumManifestBytes $Value.bounds.maximumPathCharacters
    $coverage = Read-FcNativeHashReference $Root $Node.coverage $Value.bounds.maximumReportBytes $Value.bounds.maximumPathCharacters
    $files = Assert-FcNativeContext $context.value $Manifest $Value.bounds
    Assert-FcNativeContextTool $files $context.value.tool $Value.tool $Value.bounds $ToolPackageRoot
    Assert-FcNativeTerminal $Node $terminal.value $coverage $context $Value.tool
    $identity = [ordered]@{ contextDigest = $context.file.sha256; imageId = $terminal.value.imageId
        baseReference = $context.value.baseImage.reference; baseReceiptSha256 = $context.value.baseImage.sourceReceiptSha256 }
    [ordered]@{ node = $Node.node; context = $context; terminal = $terminal; coverage = $coverage;
        imageIdentity = $identity; input = [ordered]@{ kind = 'server'; fixtureId = $Fixture.fixtureId;
            node = $Node.node; path = $coverage.path; length = $coverage.length; sha256 = $coverage.sha256 } }
}

function Assert-FcNativeRf3CaseCoverage([object] $Value, [object] $SuiteRuns, [Collections.Generic.HashSet[string]] $Associations) {
    $rf3Run = @($Value.suiteRuns | Where-Object suite -ceq 'rf3')[0]
    $expected = @($SuiteRuns.cases[[string] $rf3Run.runId] | Sort-Object)
    $actual = @($Associations | ForEach-Object { $_.Substring($_.IndexOf('|', [StringComparison]::Ordinal) + 1) } | Sort-Object)
    if ($actual.Count -ne $expected.Count -or ($actual -join "`n") -cne ($expected -join "`n")) {
        throw $script:FcNativeMergeInput.InvalidDescriptor
    }
}
