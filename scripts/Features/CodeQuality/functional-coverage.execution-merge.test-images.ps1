$script:FcTestImages = [ordered]@{
    ExpectedSuites = @('unit','recovery','cluster')
    Invalid = 'The original execution test-image manifest is absent, changed or outside its suite binding.'
}

function Read-FcTestImageManifests([string] $Root, [string] $Repository,
    [object] $SourceManifest, [object] $Bounds) {
    $entries = $SourceManifest.compiledTestsManifest
    if ($entries -isnot [array] -or $entries.Count -ne $script:FcTestImages.ExpectedSuites.Count) {
        throw $script:FcMergeInput.InvalidSource
    }
    $bySuite = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $byFile = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        Add-FcTestImageManifestEntry $Root $Repository $SourceManifest $Bounds $entry $bySuite $byFile $seen
    }
    $actual = (@($bySuite.Keys | Sort-Object) -join "`n")
    $expected = (@($script:FcTestImages.ExpectedSuites | Sort-Object) -join "`n")
    if ($actual -cne $expected) { throw $script:FcMergeInput.InvalidSource }
    Assert-FcTestImageSuiteRelationships $bySuite
    $bySuite
}

function Add-FcTestImageManifestEntry([string] $Root, [string] $Repository,
    [object] $SourceManifest, [object] $Bounds, [object] $Entry,
    [Collections.Generic.Dictionary[string, object]] $BySuite,
    [Collections.Generic.Dictionary[string, object]] $ByFile,
    [Collections.Generic.HashSet[string]] $SeenSuites) {
    Assert-FcExactKeys $Entry @('suite','fileName','sha256')
    if ($Entry.suite -cnotin $script:FcTestImages.ExpectedSuites -or
        $Entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Entry.fileName -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._-]{0,254}\z' -or
        -not $SeenSuites.Add([string] $Entry.suite)) { throw $script:FcTestImages.Invalid }
    $path = Resolve-FcEvidencePath $Root ([string] $Entry.fileName) $Bounds.maximumPathCharacters
    $read = Read-FcBoundedFile $path $Bounds.maximumManifestBytes $Bounds.readBufferBytes $script:FcTestImages.Invalid
    if ($read.sha256 -cne $Entry.sha256) { throw $script:FcTestImages.Invalid }
    if (-not $ByFile.ContainsKey($path)) {
        Add-FcTestImageManifestFile $path $read $Root $Repository $Bounds $SourceManifest $ByFile
    }
    $BySuite[[string] $Entry.suite] = $ByFile[$path]
}

function Add-FcTestImageManifestFile([string] $Path, [object] $Read,
    [string] $Root, [string] $Repository, [object] $Bounds, [object] $SourceManifest,
    [Collections.Generic.Dictionary[string, object]] $ByFile) {
    if ($script:FcMergeInput.ReferenceCatalog.Count -ge $Bounds.maximumFiles -or
        $script:FcMergeInput.ReferenceBytes -gt $Bounds.maximumTotalBytes - $Read.length) {
        throw $script:FcTestImages.Invalid
    }
    $json = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($Read.bytes))
    try { Assert-FcJsonUnique $json.RootElement }
    finally { $json.Dispose() }
    $identity = if ($Entry.suite -ceq 'unit') {
        Read-FcTestIdentityManifest $Path $Repository
    } else {
        Read-RuntimeFcTestIdentityManifest $Path $Repository ([string] $Entry.suite) $Bounds
    }
    $producer = $SourceManifest.compilationProducer
    $receiptProducer = $identity.compiledIdentity.compileReceipt.producer
    if ($receiptProducer -isnot [Collections.IDictionary] -or $receiptProducer.Count -ne 2 -or
        $receiptProducer.path -cne $producer.path -or $receiptProducer.sha256 -cne $producer.sha256) {
        throw $script:FcTestImages.Invalid
    }
    $ByFile.Add($Path, [ordered]@{ path = $Path; length = $Read.length; sha256 = $Read.sha256; identity = $identity })
    $script:FcMergeInput.ReferenceCatalog.Add($Path, [ordered]@{ length = $Read.length; sha256 = $Read.sha256 })
    $script:FcMergeInput.ReferenceBytes += $Read.length
}

function Assert-FcTestImageSuiteRelationships([Collections.Generic.Dictionary[string, object]] $BySuite) {
    $unit = $BySuite['unit']
    foreach ($suite in @('recovery','cluster')) {
        if ($BySuite[$suite].path -ceq $unit.path) {
            throw $script:FcMergeInput.InvalidSource
        }
    }
    if ($BySuite['recovery'].path -ceq $BySuite['cluster'].path) { throw $script:FcMergeInput.InvalidSource }
}


$script:FcTestImageProjects = [ordered]@{
    'unit' = [ordered]@{ project = 'tests/KeyLoad.FunctionalTests/KeyLoad.FunctionalTests.csproj'; assembly = 'KeyLoad.FunctionalTests.dll' }
    'recovery' = [ordered]@{ project = 'tests/KeyLoad.ProcessRecoveryTests/KeyLoad.ProcessRecoveryTests.csproj'; assembly = 'KeyLoad.ProcessRecoveryTests.dll' }
    'cluster' = [ordered]@{ project = 'tests/KeyLoad.ClusterTests/KeyLoad.ClusterTests.csproj'; assembly = 'KeyLoad.ClusterTests.dll' }
}

function Read-RuntimeFcTestIdentityManifest([string] $Path, [string] $Repository, [string] $Suite, [object] $Bounds) {
    if (-not $script:FcTestImageProjects.Contains($Suite)) { throw $script:FcTestImages.Invalid }
    $file = Read-FcBoundedFile $Path $Bounds.maximumManifestBytes $Bounds.readBufferBytes $script:FcTestImages.Invalid
    $json = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($file.bytes))
    try {
        Assert-FcJsonUnique $json.RootElement
        $manifest = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($file.bytes)) -AsHashtable -Depth 12
    }
    finally { $json.Dispose() }
    Assert-FcExactKeys $manifest @('schemaVersion','project','sources','buildInputs','compiledIdentity','qualification')
    $expectedProject = [string] $script:FcTestImageProjects[$Suite].project
    if ($manifest.schemaVersion -ne 1 -or $manifest.project -cne $expectedProject -or
        [string]::IsNullOrWhiteSpace([string] $manifest.qualification) -or
        $manifest.sources -isnot [array] -or $manifest.sources.Count -eq 0 -or $manifest.sources.Count -gt 5000 -or
        $manifest.buildInputs -isnot [array] -or $manifest.buildInputs.Count -ne 6 -or
        $manifest.compiledIdentity -isnot [Collections.IDictionary]) { throw $script:FcTestImages.Invalid }
    Assert-FcTestImageSources $manifest.sources $expectedProject $Repository
    Assert-FcTestImageBuildInputs $manifest.buildInputs $expectedProject $Repository
    $projectDirectory = $expectedProject.Substring(0, $expectedProject.LastIndexOf('/'))
    $assembly = [string] $script:FcTestImageProjects[$Suite].assembly
    $expectedDll = $projectDirectory + '/bin/Debug/net10.0/' + $assembly
    if ($manifest.compiledIdentity.dll -cne $expectedDll -or
        $manifest.compiledIdentity.pdb -cne [IO.Path]::ChangeExtension($expectedDll, '.pdb')) {
        throw $script:FcTestImages.Invalid
    }
    $identity = Read-FcCompiledIdentity $Repository ([string] $manifest.compiledIdentity.dll) `
        ([string] $manifest.compiledIdentity.pdb) @($manifest.sources) ([string] $manifest.compiledIdentity.originalCompilationRoot)
    if ($identity.moduleName -cne [string] $script:FcTestImageProjects[$Suite].assembly) {
        throw $script:FcTestImages.Invalid
    }
    $producer = Get-FcCompilationProducerBinding $Repository
    $dllPath = Resolve-FcPath $Repository ([string] $identity.dll)
    $pdbPath = Resolve-FcPath $Repository ([string] $identity.pdb)
    $receipt = Read-FcCompileReceipt (Resolve-FcPath $Repository ([string] $identity.dll)) `
        @($manifest.sources) @($manifest.buildInputs) $producer
    if ((Get-FcHash $dllPath) -cne $identity.dllSha256 -or (Get-FcHash $pdbPath) -cne $identity.pdbSha256) {
        throw $script:FcTestImages.Invalid
    }
    $identity['compileReceipt'] = $receipt
    if ((ConvertTo-Json -InputObject $identity -Compress -Depth 12) -cne
        (ConvertTo-Json -InputObject $manifest.compiledIdentity -Compress -Depth 12)) { throw $script:FcTestImages.Invalid }
    [ordered]@{ path = $Path; manifest = $manifest; sources = @($manifest.sources); buildInputs = @($manifest.buildInputs); compiledIdentity = $identity }
}

function Assert-FcTestImageSources([object[]] $Sources, [string] $Project, [string] $Repository) {
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Sources) {
        Assert-FcExactKeys $source @('path','sha256')
        $path = [string] $source.path
        if (-not $path.StartsWith($Project.Substring(0, $Project.LastIndexOf('/')) + '/', [StringComparison]::Ordinal) -or
            $path.Split('/') -contains 'obj' -or $path.Split('/') -contains 'bin' -or
            $source.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or -not $expected.Add($path) -or
            (Get-FcHash (Resolve-FcPath $Repository $path)) -cne $source.sha256) { throw $script:FcTestImages.Invalid }
    }
    $root = Resolve-FcPath $Repository ($Project.Substring(0, $Project.LastIndexOf('/')))
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($root)
    $entries = 0
    while ($pending.Count -gt 0) {
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            $entries++; if ($entries -gt 5000) { throw $script:FcTestImages.Invalid }
            $item = Get-Item -LiteralPath $path -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $script:FcTestImages.Invalid }
            if ($item -is [IO.DirectoryInfo]) { if ($item.Name -cnotin @('obj','bin')) { $pending.Push($item.FullName) }; continue }
            if ($item -is [IO.FileInfo] -and $item.Extension -ceq '.cs') {
                $relative = [IO.Path]::GetRelativePath($Repository, $item.FullName).Replace([IO.Path]::DirectorySeparatorChar, '/')
                if (-not $expected.Remove($relative)) { throw $script:FcTestImages.Invalid }
            }
        }
    }
    if ($expected.Count -ne 0) { throw $script:FcTestImages.Invalid }
}

function Assert-FcTestImageBuildInputs([object[]] $Inputs, [string] $Project, [string] $Repository) {
    $central = @('global.json','Directory.Build.props','Directory.Build.targets','Directory.Packages.props','KeyLoad.slnx',$Project)
    $actual = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Inputs) {
        Assert-FcExactKeys $entry @('path','sha256')
        if ($entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or $actual.ContainsKey([string] $entry.path)) {
            throw $script:FcTestImages.Invalid
        }
        $actual.Add([string] $entry.path,[string] $entry.sha256)
    }
    if (-not [Collections.Generic.HashSet[string]]::new([string[]] $actual.Keys,[StringComparer]::Ordinal).SetEquals([string[]] $central)) {
        throw $script:FcTestImages.Invalid
    }
    foreach ($path in $central) {
        if ((Get-FcHash (Resolve-FcPath $Repository $path)) -cne $actual[$path]) { throw $script:FcTestImages.Invalid }
    }
}
