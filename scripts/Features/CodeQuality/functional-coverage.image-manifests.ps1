$script:FcCoverageImages = [ordered]@{
    TestManifestName = 'functional-coverage.test-image-manifest.json'
    InvalidBinding = 'Functional coverage has no current native product and test-image binding.'
}

function Get-FcPreparedImages([string] $Root, [object] $Contract, [object[]] $Sources) {
    $dll = $Contract.deploymentDirectory + '/' + $Contract.moduleFile
    $pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
    $product = Read-FcCompiledIdentity $Root $dll $pdb $Sources
    if (-not $product.compiledSourceBindingComplete -or
        @($product.sourceFilesWithoutPdbDocuments).Count -ne 0) {
        throw $script:FcCoverageImages.InvalidBinding
    }
    $tests = Get-FcTestIdentitySnapshot $Root
    [ordered]@{ product = $product; tests = $tests }
}

function Write-FcTestImageManifest([string] $EvidenceRoot, [object] $Snapshot) {
    $path = Join-Path $EvidenceRoot $script:FcCoverageImages.TestManifestName
    if ([IO.File]::Exists($path) -or [IO.Directory]::Exists($path)) {
        throw $script:FunctionalCoverage.ErrorStale
    }
    $pending = Join-Path $EvidenceRoot ('.test-image-' + [Guid]::NewGuid().ToString('N') + '.pending')
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(
            (ConvertTo-Json -InputObject $Snapshot -Depth $script:FcTestIdentity.MaximumJsonDepth))
        if ($bytes.LongLength -gt $script:FcTestIdentity.MaximumManifestBytes) {
            throw $script:FcTestIdentity.InvalidManifest
        }
        $stream = [IO.FileStream]::new($pending, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes); $stream.Flush($true) }
        finally { $stream.Dispose() }
        [IO.File]::Move($pending, $path, $false)
        [ordered]@{ fileName = $script:FcCoverageImages.TestManifestName; sha256 = Get-FcHash $path }
    }
    finally {
        if ([IO.File]::Exists($pending)) { [IO.File]::Delete($pending) }
    }
}

function Assert-FcPreparedImages([string] $Root, [string] $EvidenceRoot, [object] $Contract,
    [object] $Manifest, [object[]] $Sources) {
    if (-not $Manifest.Contains('compiledProduct') -or -not $Manifest.Contains('compiledTestsManifest')) {
        throw $script:FcCoverageImages.InvalidBinding
    }
    $binding = $Manifest.compiledTestsManifest
    if ($binding -isnot [Collections.IDictionary] -or $binding.Count -ne 2 -or
        $binding.fileName -cne $script:FcCoverageImages.TestManifestName -or
        $binding.sha256 -isnot [string] -or $binding.sha256 -cnotmatch '\A[0-9a-f]{64}\z') {
        throw $script:FcCoverageImages.InvalidBinding
    }
    $testPath = Resolve-FcEvidenceFile $EvidenceRoot (Join-Path $EvidenceRoot $binding.fileName)
    if ((Get-FcHash $testPath) -cne $binding.sha256) { throw $script:FunctionalCoverage.ErrorDrift }
    $testIdentity = Read-FcTestIdentityManifest $testPath $Root
    $dll = $Contract.deploymentDirectory + '/' + $Contract.moduleFile
    $pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
    $product = Read-FcCompiledIdentity $Root $dll $pdb $Sources
    $current = ConvertTo-Json -InputObject $product -Depth $script:FcTestIdentity.MaximumJsonDepth -Compress
    $prepared = ConvertTo-Json -InputObject $Manifest.compiledProduct -Depth $script:FcTestIdentity.MaximumJsonDepth -Compress
    if (-not $product.compiledSourceBindingComplete -or $current -cne $prepared -or
        (Get-FcHash $testPath) -cne $binding.sha256) {
        throw $script:FunctionalCoverage.ErrorDrift
    }
}
