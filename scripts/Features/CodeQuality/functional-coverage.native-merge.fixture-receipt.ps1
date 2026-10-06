$script:FcNativeFixtureReceipt = [ordered]@{ Invalid = 'The original RF3 fixture receipt is incomplete or mismatched.' }

function Assert-FcNativeFixtureReceipt([string] $Root, [object] $Fixture, [object] $Manifest,
    [object] $Run, [object[]] $Nodes, [object] $Bounds) {
    $read = Read-FcNativeJsonReference $Root $Fixture.fixtureReceipt $Bounds.maximumManifestBytes $Bounds.maximumPathCharacters
    $receipt = $read.value
    Assert-FcNativeExactKeys $receipt @('schemaVersion','fixtureId','functionalRunId','suite','sourceRevision',
        'sourceManifestSha256','caseIdentities','sourceImage','coverageImage','server','collector','nodes')
    if ($receipt.schemaVersion -ne 1 -or $receipt.fixtureId -cne $Fixture.fixtureId -or
        $receipt.functionalRunId -cne $Fixture.functionalRunId -or $receipt.functionalRunId -cne $Run.runId -or
        $receipt.suite -cne 'rf3' -or $Run.suite -cne 'rf3' -or
        $receipt.sourceRevision -cne $Manifest.value.sourceRevision -or
        $receipt.sourceManifestSha256 -cne $Fixture.sourceManifestSha256 -or
        $read.file.sha256 -cne $Fixture.fixtureReceipt.sha256) { throw $script:FcNativeFixtureReceipt.Invalid }
    Assert-FcNativeFixtureCaseIdentities $receipt.caseIdentities $Fixture.caseIdentities
    Assert-FcNativeFixtureImages $Root $receipt $Fixture $Nodes $Bounds $Manifest
    Assert-FcNativeFixtureServer $receipt.server $Nodes $Manifest.file.sha256
    Assert-FcNativeFixtureCollector $receipt.collector $Nodes $Bounds
    Assert-FcNativeFixtureNodes $receipt.nodes $Fixture.nodes $Nodes
}

function Assert-FcNativeFixtureCaseIdentities([object[]] $Actual, [object[]] $Expected) {
    if ($Actual -isnot [array] -or $Expected -isnot [array] -or $Actual.Count -ne $Expected.Count) {
        throw $script:FcNativeFixtureReceipt.Invalid
    }
    $actualKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($identity in $Actual) {
        Assert-FcNativeExactKeys $identity @('className','methodName','instanceName')
        foreach ($name in @('className','methodName','instanceName')) {
            if ($identity[$name] -isnot [string] -or [string]::IsNullOrWhiteSpace($identity[$name]) -or
                $identity[$name].Length -gt 512) { throw $script:FcNativeFixtureReceipt.Invalid }
        }
        if (-not $actualKeys.Add((Get-FcNativeTrxKey $identity.className $identity.methodName $identity.instanceName))) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
    }
    $expectedKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($identity in $Expected) {
        if ($identity -isnot [string] -or -not $expectedKeys.Add([string] $identity)) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
    }
    if (-not $actualKeys.SetEquals([string[]] $expectedKeys)) { throw $script:FcNativeFixtureReceipt.Invalid }
}

function Assert-FcNativeFixtureImages([string] $Root, [object] $Receipt, [object] $Fixture,
    [object[]] $Nodes, [object] $Bounds, [object] $Manifest) {
    $source = $Receipt.sourceImage
    Assert-FcNativeExactKeys $source @('reference','manifestDigest','sourceReceiptSha256')
    $coverage = $Receipt.coverageImage
    Assert-FcNativeExactKeys $coverage @('reference','imageId','contextManifestSha256','dockerfileSha256',
        'materializerReceipt','inspectReceipt')
    $sourceDigest = Get-FcNativeImageDigest ([string] $source.reference)
    $expectedCoverageReference = 'keyload/functional-coverage:' + ([string] $Fixture.functionalRunId).Replace('-', '').ToLowerInvariant()
    if ($coverage.reference -cne $expectedCoverageReference -or
        $source.manifestDigest -cne $sourceDigest -or $coverage.imageId -cnotmatch '\Asha256:[0-9a-f]{64}\z' -or
        $source.sourceReceiptSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $coverage.contextManifestSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $coverage.dockerfileSha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeFixtureReceipt.Invalid }
    $materializer = Read-FcNativeJsonReference $Root $coverage.materializerReceipt $Bounds.maximumManifestBytes $Bounds.maximumPathCharacters
    Assert-FcNativeExactKeys $materializer.value @('contextDirectory','manifestPath','manifestSha256','fileCount','totalBytes')
    $contextDirectory = Assert-FcNativeMaterializerDirectory $materializer.value $Bounds
    if ($materializer.value.manifestSha256 -cne $coverage.contextManifestSha256 -or
        $materializer.value.fileCount -isnot [int] -and $materializer.value.fileCount -isnot [long] -or
        $materializer.value.totalBytes -isnot [int] -and $materializer.value.totalBytes -isnot [long]) {
        throw $script:FcNativeFixtureReceipt.Invalid
    }
    $inspect = Read-FcNativeJsonReference $Root $coverage.inspectReceipt $Bounds.maximumManifestBytes $Bounds.maximumPathCharacters
    Assert-FcNativeFixtureInspect $inspect.value $coverage $Nodes
    Assert-FcNativeServerImageClosure $contextDirectory $Nodes[0].evidence.context.value $Manifest $Bounds `
        $coverage.dockerfileSha256 $Nodes[0].evidence.context.file
    foreach ($entry in $Nodes) {
        $context = $entry.evidence.context.value
        $renderedDockerfile = @($context.files | Where-Object path -ceq 'Dockerfile')
        if ($context.server.sourceReceiptSha256 -cne $Manifest.file.sha256 -or
            $coverage.imageId -cne $entry.evidence.terminal.value.imageId -or
            $coverage.contextManifestSha256 -cne $entry.evidence.context.file.sha256 -or
            $renderedDockerfile.Count -ne 1 -or $coverage.dockerfileSha256 -cne $renderedDockerfile[0].sha256 -or
            $materializer.value.fileCount -ne ($context.files.Count + 1) -or
            $materializer.value.totalBytes -ne ([long] (($context.files | Measure-Object -Property length -Sum).Sum) +
                [long] $entry.evidence.context.file.length)) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
    }
    if ($materializer.value.contextDirectory -cne $contextDirectory -or
        [IO.Path]::GetFullPath([string] $materializer.value.manifestPath) -cne (Join-Path $contextDirectory 'context-manifest.json')) {
        throw $script:FcNativeFixtureReceipt.Invalid
    }
}

function Assert-FcNativeMaterializerDirectory([object] $Materializer, [object] $Bounds) {
    foreach ($name in @('contextDirectory','manifestPath')) {
        if ($Materializer[$name] -isnot [string] -or [string]::IsNullOrWhiteSpace($Materializer[$name]) -or
            $Materializer[$name].Length -gt $Bounds.maximumPathCharacters -or
            -not [IO.Path]::IsPathFullyQualified($Materializer[$name])) { throw $script:FcNativeFixtureReceipt.Invalid }
    }
    $directory = [IO.Path]::GetFullPath([string] $Materializer.contextDirectory)
    Assert-FcNativeNoReparsePath $directory
    if (-not [IO.Directory]::Exists($directory)) { throw $script:FcNativeFixtureReceipt.Invalid }
    $directory
}

function Assert-FcNativeFixtureInspect([object] $Inspect, [object] $Coverage, [object[]] $Nodes) {
    if ($Inspect -isnot [string] -or $Inspect -cne $Coverage.imageId) {
        throw $script:FcNativeFixtureReceipt.Invalid
    }
    foreach ($entry in $Nodes) {
        if ($Inspect -cne $entry.evidence.terminal.value.imageId) { throw $script:FcNativeFixtureReceipt.Invalid }
    }
}

function Get-FcNativeImageDigest([string] $Reference) {
    if ($Reference -notmatch '\A[^\s@]{1,512}@(?<digest>sha256:[0-9a-f]{64})\z') {
        throw $script:FcNativeFixtureReceipt.Invalid
    }
    $Matches.digest
}

function Assert-FcNativeFixtureServer([object] $Server, [object[]] $Nodes, [string] $ManifestSha256) {
    Assert-FcNativeExactKeys $Server @('assemblyName','mvid','dllSha256','pdbSha256','sourceReceiptSha256')
    if ($Server.assemblyName -cne 'KeyLoad.Server' -or $Server.mvid -cnotmatch '\A[0-9a-fA-F-]{36}\z' -or
        $Server.dllSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or $Server.pdbSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Server.sourceReceiptSha256 -cne $ManifestSha256) { throw $script:FcNativeFixtureReceipt.Invalid }
    foreach ($entry in $Nodes) {
        $context = $entry.evidence.context.value
        if ($Server.mvid -cne $context.server.mvid -or $Server.dllSha256 -cne $context.server.dllSha256 -or
            $Server.pdbSha256 -cne $context.server.pdbSha256 -or
            $Server.sourceReceiptSha256 -cne $context.server.sourceReceiptSha256) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
    }
}

function Assert-FcNativeFixtureCollector([object] $Collector, [object[]] $Nodes, [object] $MergeBounds) {
    Assert-FcNativeExactKeys $Collector @('packageId','version','closureDigest','settingsSha256','bounds')
    if ($Collector.packageId -cne 'dotnet-coverage' -or $Collector.version -cne '18.11.2' -or
        $Collector.closureDigest -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Collector.settingsSha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeFixtureReceipt.Invalid }
    foreach ($entry in $Nodes) {
        $context = $entry.evidence.context.value
        if ($Collector.version -cne $context.tool.version -or
            $Collector.closureDigest -cne $context.tool.closureDigest -or
            $Collector.settingsSha256 -cne $context.sourceTemplates.settingsSha256) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
        Assert-FcNativeFixtureBounds $Collector.bounds $MergeBounds
    }
}

function Assert-FcNativeFixtureBounds([object] $Actual, [object] $Expected) {
    Assert-FcNativeExactKeys $Actual @('maximumDescriptorBytes','maximumFiles','readBufferBytes','maximumTotalBytes',
        'maximumFileBytes','maximumPathCharacters','maximumManifestBytes','maximumReportBytes','shutdownTimeout',
        'settlementTimeout','containerStopTimeout','applicationCleanupTimeout')
    foreach ($name in @('maximumDescriptorBytes','maximumFiles','readBufferBytes','maximumTotalBytes','maximumFileBytes',
        'maximumPathCharacters','maximumManifestBytes','maximumReportBytes')) {
        if (($Actual[$name] -isnot [int] -and $Actual[$name] -isnot [long]) -or $Actual[$name] -ne $Expected[$name]) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
    }
    foreach ($name in @('shutdownTimeout','settlementTimeout','containerStopTimeout','applicationCleanupTimeout')) {
        $secondsName = $name + 'Seconds'
        $seconds = [int] $Expected[$secondsName]
        $canonical = [TimeSpan]::FromSeconds($seconds).ToString('c', [Globalization.CultureInfo]::InvariantCulture)
        if ($Actual[$name] -isnot [string] -or $Actual[$name] -cne $canonical) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
    }
}

function Assert-FcNativeFixtureNodes([object[]] $Actual, [object[]] $Expected, [object[]] $Evidence) {
    if ($Actual -isnot [array] -or $Actual.Count -ne 3 -or $Expected.Count -ne 3 -or $Evidence.Count -ne 3) {
        throw $script:FcNativeFixtureReceipt.Invalid
    }
    $expectedByNode = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $evidenceByNode = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($item in $Expected) { $expectedByNode.Add([string] $item.node, $item) }
    foreach ($item in $Evidence) { $evidenceByNode.Add([string] $item.descriptor.node, $item) }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($node in $Actual) {
        Assert-FcNativeExactKeys $node @('node','containerId','imageId','session','serverPid','serverStartTicks',
            'terminal','coverage','contextManifest')
        if ($node.node -cnotin @('node1','node2','node3') -or -not $seen.Add([string] $node.node) -or
            $node.containerId -cnotmatch '\A[0-9a-f]{12,64}\z' -or $node.imageId -cnotmatch '\Asha256:[0-9a-f]{64}\z' -or
            $node.serverPid -isnot [long] -and $node.serverPid -isnot [int] -or $node.serverPid -le 0 -or
            $node.serverStartTicks -isnot [long] -and $node.serverStartTicks -isnot [int] -or $node.serverStartTicks -le 0) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
        $expected = $expectedByNode[[string] $node.node]
        $evidence = $evidenceByNode[[string] $node.node]
        $terminal = $evidence.evidence.terminal.value
        if ($node.session -cne $expected.session -or $node.imageId -cne $terminal.imageId -or
            $node.serverPid -ne $terminal.serverProcess.pid -or $node.serverStartTicks -ne $terminal.serverProcess.startTicks -or
            -not (Test-FcNativeSameReference $node.terminal $expected.terminal) -or
            -not (Test-FcNativeSameReference $node.coverage $expected.coverage) -or
            -not (Test-FcNativeSameReference $node.contextManifest $expected.contextManifest)) {
            throw $script:FcNativeFixtureReceipt.Invalid
        }
    }
    if (-not $seen.SetEquals([string[]] @('node1','node2','node3'))) { throw $script:FcNativeFixtureReceipt.Invalid }
}

function Test-FcNativeSameReference([object] $Left, [object] $Right) {
    Assert-FcNativeExactKeys $Left @('path','length','sha256')
    Assert-FcNativeExactKeys $Right @('path','length','sha256')
    $Left.path -ceq $Right.path -and $Left.length -eq $Right.length -and $Left.sha256 -ceq $Right.sha256
}
