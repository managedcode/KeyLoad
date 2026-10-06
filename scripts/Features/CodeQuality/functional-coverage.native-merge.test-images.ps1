$script:FcNativeTestImages = [ordered]@{
    ExpectedSuites = @('unit','unit-scalar','recovery','rf3')
    Invalid = 'The original native test-image manifest is absent, changed or outside its suite binding.'
}

function Read-FcNativeTestImageManifests([string] $Root, [string] $Repository,
    [object] $SourceManifest, [object] $Bounds) {
    $entries = $SourceManifest.compiledTestsManifest
    if ($entries -isnot [array] -or $entries.Count -ne $script:FcNativeTestImages.ExpectedSuites.Count) {
        throw $script:FcNativeMergeInput.InvalidSource
    }
    $bySuite = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $byFile = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        Add-FcNativeTestImageManifestEntry $Root $Repository $SourceManifest $Bounds $entry $bySuite $byFile $seen
    }
    $actual = (@($bySuite.Keys | Sort-Object) -join "`n")
    $expected = (@($script:FcNativeTestImages.ExpectedSuites | Sort-Object) -join "`n")
    if ($actual -cne $expected) { throw $script:FcNativeMergeInput.InvalidSource }
    Assert-FcNativeTestImageSuiteRelationships $bySuite
    $bySuite
}

function Add-FcNativeTestImageManifestEntry([string] $Root, [string] $Repository,
    [object] $SourceManifest, [object] $Bounds, [object] $Entry,
    [Collections.Generic.Dictionary[string, object]] $BySuite,
    [Collections.Generic.Dictionary[string, object]] $ByFile,
    [Collections.Generic.HashSet[string]] $SeenSuites) {
    Assert-FcNativeExactKeys $Entry @('suite','fileName','sha256')
    if ($Entry.suite -cnotin $script:FcNativeTestImages.ExpectedSuites -or
        $Entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Entry.fileName -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._-]{0,254}\z' -or
        -not $SeenSuites.Add([string] $Entry.suite)) { throw $script:FcNativeTestImages.Invalid }
    $path = Resolve-FcNativeEvidencePath $Root ([string] $Entry.fileName) $Bounds.maximumPathCharacters
    $read = Read-FcNativeBoundedFile $path $Bounds.maximumManifestBytes $Bounds.readBufferBytes $script:FcNativeTestImages.Invalid
    if ($read.sha256 -cne $Entry.sha256) { throw $script:FcNativeTestImages.Invalid }
    if (-not $ByFile.ContainsKey($path)) {
        Add-FcNativeTestImageManifestFile $path $read $Root $Repository $Bounds $SourceManifest $ByFile
    }
    $BySuite[[string] $Entry.suite] = $ByFile[$path]
}

function Add-FcNativeTestImageManifestFile([string] $Path, [object] $Read,
    [string] $Root, [string] $Repository, [object] $Bounds, [object] $SourceManifest,
    [Collections.Generic.Dictionary[string, object]] $ByFile) {
    if ($script:FcNativeMergeInput.ReferenceCatalog.Count -ge $Bounds.maximumFiles -or
        $script:FcNativeMergeInput.ReferenceBytes -gt $Bounds.maximumTotalBytes - $Read.length) {
        throw $script:FcNativeTestImages.Invalid
    }
    $json = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($Read.bytes))
    try { Assert-FcNativeJsonUnique $json.RootElement }
    finally { $json.Dispose() }
    $identity = if ($Entry.suite -cin @('unit','unit-scalar')) {
        Read-FcTestIdentityManifest $Path $Repository
    } else {
        Read-FcNativeTestIdentityManifest $Path $Repository ([string] $Entry.suite) $Bounds
    }
    $producer = $SourceManifest.compilationProducer
    $receiptProducer = $identity.compiledIdentity.compileReceipt.producer
    if ($receiptProducer -isnot [Collections.IDictionary] -or $receiptProducer.Count -ne 2 -or
        $receiptProducer.path -cne $producer.path -or $receiptProducer.sha256 -cne $producer.sha256) {
        throw $script:FcNativeTestImages.Invalid
    }
    $ByFile.Add($Path, [ordered]@{ path = $Path; length = $Read.length; sha256 = $Read.sha256; identity = $identity })
    $script:FcNativeMergeInput.ReferenceCatalog.Add($Path, [ordered]@{ length = $Read.length; sha256 = $Read.sha256 })
    $script:FcNativeMergeInput.ReferenceBytes += $Read.length
}

function Assert-FcNativeTestImageSuiteRelationships([Collections.Generic.Dictionary[string, object]] $BySuite) {
    $unit = $BySuite['unit']; $scalar = $BySuite['unit-scalar']
    if ($unit.path -cne $scalar.path -or $unit.sha256 -cne $scalar.sha256) {
        throw $script:FcNativeMergeInput.InvalidSource
    }
    foreach ($suite in @('recovery','rf3')) {
        if ($BySuite[$suite].path -ceq $unit.path -or $BySuite[$suite].path -ceq $scalar.path) {
            throw $script:FcNativeMergeInput.InvalidSource
        }
    }
    if ($BySuite['recovery'].path -ceq $BySuite['rf3'].path) { throw $script:FcNativeMergeInput.InvalidSource }
}


$script:FcNativeTestImageProjects = [ordered]@{
    'unit' = [ordered]@{ project = 'tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj'; assembly = 'KeyLoad.UnitTests.dll' }
    'unit-scalar' = [ordered]@{ project = 'tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj'; assembly = 'KeyLoad.UnitTests.dll' }
    'recovery' = [ordered]@{ project = 'tests/KeyLoad.RecoveryTests/KeyLoad.RecoveryTests.csproj'; assembly = 'KeyLoad.RecoveryTests.dll' }
    'rf3' = [ordered]@{ project = 'tests/KeyLoad.IntegrationTests/KeyLoad.IntegrationTests.csproj'; assembly = 'KeyLoad.IntegrationTests.dll' }
}

function Read-FcNativeTestIdentityManifest([string] $Path, [string] $Repository, [string] $Suite, [object] $Bounds) {
    if (-not $script:FcNativeTestImageProjects.Contains($Suite)) { throw $script:FcNativeTestImages.Invalid }
    $file = Read-FcNativeBoundedFile $Path $Bounds.maximumManifestBytes $Bounds.readBufferBytes $script:FcNativeTestImages.Invalid
    $json = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($file.bytes))
    try {
        Assert-FcNativeJsonUnique $json.RootElement
        $manifest = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($file.bytes)) -AsHashtable -Depth 12
    }
    finally { $json.Dispose() }
    Assert-FcNativeExactKeys $manifest @('schemaVersion','project','sources','buildInputs','compiledIdentity','qualification')
    $expectedProject = [string] $script:FcNativeTestImageProjects[$Suite].project
    if ($manifest.schemaVersion -ne 1 -or $manifest.project -cne $expectedProject -or
        [string]::IsNullOrWhiteSpace([string] $manifest.qualification) -or
        $manifest.sources -isnot [array] -or $manifest.sources.Count -eq 0 -or $manifest.sources.Count -gt 5000 -or
        $manifest.buildInputs -isnot [array] -or $manifest.buildInputs.Count -ne 6 -or
        $manifest.compiledIdentity -isnot [Collections.IDictionary]) { throw $script:FcNativeTestImages.Invalid }
    Assert-FcNativeTestImageSources $manifest.sources $expectedProject $Repository
    Assert-FcNativeTestImageBuildInputs $manifest.buildInputs $expectedProject $Repository
    $projectDirectory = $expectedProject.Substring(0, $expectedProject.LastIndexOf('/'))
    $assembly = [string] $script:FcNativeTestImageProjects[$Suite].assembly
    $expectedDll = $projectDirectory + '/bin/Release/net10.0/' + $assembly
    if ($manifest.compiledIdentity.dll -cne $expectedDll -or
        $manifest.compiledIdentity.pdb -cne [IO.Path]::ChangeExtension($expectedDll, '.pdb')) {
        throw $script:FcNativeTestImages.Invalid
    }
    $identity = Read-FcCompiledIdentity $Repository ([string] $manifest.compiledIdentity.dll) `
        ([string] $manifest.compiledIdentity.pdb) @($manifest.sources) ([string] $manifest.compiledIdentity.originalCompilationRoot)
    if ($identity.moduleName -cne [string] $script:FcNativeTestImageProjects[$Suite].assembly) {
        throw $script:FcNativeTestImages.Invalid
    }
    $producer = Get-FcCompilationProducerBinding $Repository
    $dllPath = Resolve-FcPath $Repository ([string] $identity.dll)
    $pdbPath = Resolve-FcPath $Repository ([string] $identity.pdb)
    $receipt = Read-FcNativeCompileReceipt (Resolve-FcPath $Repository ([string] $identity.dll)) `
        @($manifest.sources) @($manifest.buildInputs) $producer
    if ((Get-FcHash $dllPath) -cne $identity.dllSha256 -or (Get-FcHash $pdbPath) -cne $identity.pdbSha256) {
        throw $script:FcNativeTestImages.Invalid
    }
    $identity['compileReceipt'] = $receipt
    if ((ConvertTo-Json -InputObject $identity -Compress -Depth 12) -cne
        (ConvertTo-Json -InputObject $manifest.compiledIdentity -Compress -Depth 12)) { throw $script:FcNativeTestImages.Invalid }
    [ordered]@{ path = $Path; manifest = $manifest; sources = @($manifest.sources); buildInputs = @($manifest.buildInputs); compiledIdentity = $identity }
}

function Assert-FcNativeTestImageSources([object[]] $Sources, [string] $Project, [string] $Repository) {
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Sources) {
        Assert-FcNativeExactKeys $source @('path','sha256')
        $path = [string] $source.path
        if (-not $path.StartsWith($Project.Substring(0, $Project.LastIndexOf('/')) + '/', [StringComparison]::Ordinal) -or
            $path.Split('/') -contains 'obj' -or $path.Split('/') -contains 'bin' -or
            $source.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or -not $expected.Add($path) -or
            (Get-FcHash (Resolve-FcPath $Repository $path)) -cne $source.sha256) { throw $script:FcNativeTestImages.Invalid }
    }
    $root = Resolve-FcPath $Repository ($Project.Substring(0, $Project.LastIndexOf('/')))
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($root)
    $entries = 0
    while ($pending.Count -gt 0) {
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            $entries++; if ($entries -gt 5000) { throw $script:FcNativeTestImages.Invalid }
            $item = Get-Item -LiteralPath $path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $script:FcNativeTestImages.Invalid }
            if ($item -is [IO.DirectoryInfo]) { if ($item.Name -cnotin @('obj','bin')) { $pending.Push($item.FullName) }; continue }
            if ($item -is [IO.FileInfo] -and $item.Extension -ceq '.cs') {
                $relative = [IO.Path]::GetRelativePath($Repository, $item.FullName).Replace([IO.Path]::DirectorySeparatorChar, '/')
                if (-not $expected.Remove($relative)) { throw $script:FcNativeTestImages.Invalid }
            }
        }
    }
    if ($expected.Count -ne 0) { throw $script:FcNativeTestImages.Invalid }
}

function Assert-FcNativeTestImageBuildInputs([object[]] $Inputs, [string] $Project, [string] $Repository) {
    $central = @('global.json','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','KeyLoad.slnx',$Project)
    $actual = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Inputs) {
        Assert-FcNativeExactKeys $entry @('path','sha256')
        if ($entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or $actual.ContainsKey([string] $entry.path)) {
            throw $script:FcNativeTestImages.Invalid
        }
        $actual.Add([string] $entry.path,[string] $entry.sha256)
    }
    if (-not [Collections.Generic.HashSet[string]]::new([string[]] $actual.Keys,[StringComparer]::Ordinal).SetEquals([string[]] $central)) {
        throw $script:FcNativeTestImages.Invalid
    }
    foreach ($path in $central) {
        if ((Get-FcHash (Resolve-FcPath $Repository $path)) -cne $actual[$path]) { throw $script:FcNativeTestImages.Invalid }
    }
}
