function Assert-FcCurrentSourceGeneration([object] $Plan) {
    if ((Get-FcRevision $Repository) -cne $Plan.manifest.sourceRevision -or
        (Get-FcHash (Resolve-FcPath $Repository 'scripts/Features/CodeQuality/functional-coverage.contract.json')) -cne $Plan.manifest.contractSha256) {
        throw $script:FcMergeInput.InvalidSource
    }
    Assert-FcSourceManifest $Repository $Plan.manifest
    Assert-FcProductSourceTrees $Plan.manifest $Plan.descriptor.bounds
    Assert-FcTestSourceImages $Plan.images $Plan.descriptor.bounds
}

function Assert-FcProductSourceTrees([object] $Manifest, [object] $Bounds) {
    foreach ($product in $Manifest.compiledProducts) {
        $directory = Get-FcProjectSourceDirectory ([string] $product.module) $false
        Assert-FcSourceTree $directory @($product.sources) $Bounds
    }
}

function Assert-FcTestSourceImages([object] $Images, [object] $Bounds) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($suite in @('unit','recovery','cluster')) {
        $image = $Images[$suite]
        if (-not $seen.Add([string] $image.path)) { continue }
        $identity = $image.identity
        $projectDirectory = [IO.Path]::GetDirectoryName([string] $identity.manifest.project).Replace('\', '/')
        Assert-FcSourceTree $projectDirectory @($identity.sources) $Bounds
        Assert-FcTestCompiledImage $identity $Bounds
    }
}

function Assert-FcTestCompiledImage([object] $Identity, [object] $Bounds) {
    $compiled = $Identity.compiledIdentity
    $actual = Read-FcCompiledIdentity $Repository ([string] $compiled.dll) ([string] $compiled.pdb) `
        @($Identity.sources) ([string] $compiled.originalCompilationRoot)
    $expectedKeys = @($actual.Keys | Sort-Object)
    foreach ($name in $expectedKeys) {
        if ((ConvertTo-Json -InputObject $actual[$name] -Compress -Depth 12) -cne
            (ConvertTo-Json -InputObject $compiled[$name] -Compress -Depth 12)) { throw $script:FcMergeInput.InvalidSource }
    }
    $producer = Get-FcCompilationProducerBinding $Repository
    $receipt = Read-FcCompileReceipt (Resolve-FcPath $Repository ([string] $compiled.dll)) `
        @($Identity.sources) @($Identity.buildInputs) $producer
    if ((ConvertTo-Json -InputObject $receipt -Compress -Depth 12) -cne
        (ConvertTo-Json -InputObject $compiled.compileReceipt -Compress -Depth 12)) {
        throw $script:FcMergeInput.InvalidSource
    }
}

function Assert-FcSourceTree([string] $RelativeDirectory, [object[]] $ExpectedSources, [object] $Bounds) {
    $root = Resolve-FcPath $Repository $RelativeDirectory
    if (-not [IO.Directory]::Exists($root)) { throw $script:FcMergeInput.InvalidSource }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $ExpectedSources) { [void] $expected.Add([string] $source.path) }
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($root)
    $entryCount = 0
    while ($pending.Count -gt 0) {
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            $entryCount++
            if ($entryCount -gt $Bounds.maximumFiles) { throw $script:FcMergeInput.InvalidSource }
            Assert-FcSourceTreeEntry $Repository $path $pending $expected
        }
    }
    if ($expected.Count -ne 0) { throw $script:FcMergeInput.InvalidSource }
}

function Assert-FcSourceTreeEntry([string] $RepositoryRoot, [string] $Path,
    [Collections.Generic.Stack[string]] $Pending, [Collections.Generic.HashSet[string]] $Expected) {
    $entry = Get-Item -LiteralPath $Path -Force
    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $script:FcMergeInput.InvalidSource }
    if ($entry -is [IO.DirectoryInfo]) {
        if ($entry.Name -cnotin @('obj','bin')) { $Pending.Push($entry.FullName) }
        return
    }
    if ($entry -is [IO.FileInfo] -and $entry.Extension -ceq '.cs') {
        $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $entry.FullName).Replace([IO.Path]::DirectorySeparatorChar, '/')
        if (-not $Expected.Remove($relative)) { throw $script:FcMergeInput.InvalidSource }
    }
}
