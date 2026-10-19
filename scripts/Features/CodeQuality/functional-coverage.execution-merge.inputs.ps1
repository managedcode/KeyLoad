$script:FcMergeInput = [ordered]@{
    DescriptorVersion = 1
    SourceManifestVersion = 3
    ReadBufferBytes = 0
    MaximumFiles = 0
    MaximumTotalBytes = 0
    MaximumFileBytes = 0
    ReferenceCatalog = $null
    ReferenceBytes = 0L
    ScriptHashes = $null
    RuntimeToolVersion = '18.12.0'
    ToolPackageId = 'dotnet-coverage'
    ClusterNodeCount = 3
    MaximumJsonDepth = 32
    InvalidDescriptor = 'The execution coverage merge descriptor is invalid.'
    InvalidEvidence = 'A execution coverage merge input is absent, changed or outside its evidence root.'
    InvalidSource = 'The execution coverage merge source identity is incomplete or stale.'
    InvalidReceipt = 'An original execution coverage terminal receipt is incomplete or mismatched.'
    ProcessAttributionOpen = 'Execution server-process coverage attribution is unverified: aggregate Server hits and lifecycle originals do not prove collection from every observed PID and start identity.'
    ModuleRoster = @()
}

function Assert-FcExactKeys([Collections.IDictionary] $Value, [string[]] $Names) {
    if ($null -eq $Value -or $Value.Count -ne $Names.Count) { throw $script:FcMergeInput.InvalidDescriptor }
    foreach ($name in $Names) { if (-not $Value.Contains($name)) { throw $script:FcMergeInput.InvalidDescriptor } }
}

function Assert-FcJsonUnique([System.Text.Json.JsonElement] $Element, [int] $Depth = 0) {
    if ($Depth -gt $script:FcMergeInput.MaximumJsonDepth) { throw $script:FcMergeInput.InvalidDescriptor }
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw $script:FcMergeInput.InvalidDescriptor }
            Assert-FcJsonUnique $property.Value ($Depth + 1)
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($child in $Element.EnumerateArray()) { Assert-FcJsonUnique $child ($Depth + 1) }
    }
}

function Read-FcBoundedFile([string] $Path, [long] $MaximumBytes, [int] $ReadBufferBytes, [string] $Failure) {
    if (-not [IO.File]::Exists($Path)) { throw $Failure }
    $before = Get-Item -LiteralPath $Path -Force
    if ($before -isnot [IO.FileInfo] -or $before.Length -le 0 -or $before.Length -gt $MaximumBytes -or
        ($before.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw $Failure }
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $primaryFailure = $null
    $bytes = $null
    try {
        if ($stream.Length -ne $before.Length) { throw $Failure }
        $bytes = [byte[]]::new([int] $before.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, [Math]::Min($bytes.Length - $offset, $ReadBufferBytes))
            if ($read -le 0) { throw $Failure }
            $offset += $read
        }
        if ($stream.ReadByte() -ne -1 -or $stream.Length -ne $before.Length) { throw $Failure }
    }
    catch [System.Exception] { $primaryFailure = $_.Exception }
    finally {
        try { $stream.Dispose() }
        catch [System.Exception] {
            if ($null -eq $primaryFailure) { $primaryFailure = $_.Exception }
            else { $primaryFailure = [AggregateException]::new($primaryFailure, $_.Exception) }
        }
    }
    if ($null -ne $primaryFailure) { throw $primaryFailure }
    [ordered]@{ bytes = $bytes; length = $before.Length; sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
}

function Assert-FcNoReparsePath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $current = [IO.Path]::GetPathRoot($full)
    foreach ($segment in $full.Substring($current.Length).Split([char[]]@([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw $script:FcMergeInput.InvalidEvidence }
        }
    }
}

function Resolve-FcEvidencePath([string] $Root, [string] $Relative, [int] $MaximumCharacters) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative.Length -gt $MaximumCharacters -or
        [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains('\') -or
        $Relative.Split('/') -contains '..' -or $Relative.Split('/') -contains '.' -or
        $Relative -match '[\x00-\x1f\x7f]') { throw $script:FcMergeInput.InvalidEvidence }
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, $Relative))
    $relativePath = [IO.Path]::GetRelativePath($Root, $full)
    if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath -eq '..' -or
        $relativePath.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw $script:FcMergeInput.InvalidEvidence
    }
    $current = $Root
    foreach ($part in $relativePath.Split([IO.Path]::DirectorySeparatorChar)) {
        $current = Join-Path $current $part
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw $script:FcMergeInput.InvalidEvidence
        }
    }
    $full
}

function Read-RuntimeFcEvidenceFile([string] $Root, [object] $Reference, [long] $MaximumBytes, [int] $MaximumPathCharacters) {
    Assert-FcExactKeys $Reference @('path','length','sha256')
    if ($Reference.length -isnot [long] -and $Reference.length -isnot [int] -or
        $Reference.length -le 0 -or $Reference.length -gt $MaximumBytes -or
        $Reference.sha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcMergeInput.InvalidEvidence }
    $path = Resolve-FcEvidencePath $Root ([string] $Reference.path) $MaximumPathCharacters
    $read = Read-FcBoundedFile $path $MaximumBytes $script:FcMergeInput.ReadBufferBytes $script:FcMergeInput.InvalidEvidence
    if ($read.length -ne $Reference.length -or $read.sha256 -cne $Reference.sha256) { throw $script:FcMergeInput.InvalidEvidence }
    if (-not $script:FcMergeInput.ReferenceCatalog.ContainsKey($path)) {
        if ($script:FcMergeInput.ReferenceCatalog.Count -ge $script:FcMergeInput.MaximumFiles -or
            $script:FcMergeInput.ReferenceBytes -gt $script:FcMergeInput.MaximumTotalBytes - $read.length) {
            throw $script:FcMergeInput.InvalidEvidence
        }
        $script:FcMergeInput.ReferenceCatalog.Add($path, [ordered]@{ length = $read.length; sha256 = $read.sha256 })
        $script:FcMergeInput.ReferenceBytes += $read.length
    }
    elseif ($script:FcMergeInput.ReferenceCatalog[$path].length -ne $read.length -or
        $script:FcMergeInput.ReferenceCatalog[$path].sha256 -cne $read.sha256) { throw $script:FcMergeInput.InvalidEvidence }
    [ordered]@{ path = $path; relativePath = [string] $Reference.path; bytes = $read.bytes; length = $read.length; sha256 = $read.sha256 }
}

function Read-FcJsonReference([string] $Root, [object] $Reference, [long] $MaximumBytes, [int] $MaximumPathCharacters) {
    $file = Read-RuntimeFcEvidenceFile $Root $Reference $MaximumBytes $MaximumPathCharacters
    try {
        $json = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($file.bytes))
        Assert-FcJsonUnique $json.RootElement
        $value = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($file.bytes)) -AsHashtable -DateKind String -Depth $script:FcMergeInput.MaximumJsonDepth
        return [ordered]@{ file = $file; value = $value }
    }
    catch [System.Exception] { throw $script:FcMergeInput.InvalidEvidence }
    finally { if ($null -ne $json) { $json.Dispose() } }
}

function Assert-FcSourceManifest([string] $Repository, [object] $Manifest) {
    $roster = @(Get-FcCompiledProjectRoster $Repository)
    $script:FcMergeInput.ModuleRoster = @($roster | ForEach-Object name)
    Assert-FcExactKeys $Manifest @('schemaVersion','sourceRevision','repository','contractSha256',
        'compiledProducts','compiledTestsManifest','compilationProducer','contributors','settingsSha256','scripts')
    if ($Manifest.schemaVersion -ne $script:FcMergeInput.SourceManifestVersion -or
        $Manifest.sourceRevision -cnotmatch '\A[0-9a-f]{40}\z' -or
        $Manifest.repository -cne 'managedcode/KeyLoad' -or
        $Manifest.contractSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Manifest.settingsSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Manifest.compiledProducts.Count -ne $script:FcMergeInput.ModuleRoster.Count) {
        throw $script:FcMergeInput.InvalidSource
    }
    $producer = $Manifest.compilationProducer
    $currentProducer = Get-FcCompilationProducerBinding $Repository
    if ($producer -isnot [Collections.IDictionary] -or $producer.Count -ne 2 -or
        $producer.path -cne $currentProducer.path -or $producer.sha256 -cne $currentProducer.sha256) {
        throw $script:FcMergeInput.InvalidSource
    }
    $modules = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($product in $Manifest.compiledProducts) {
        Assert-FcExactKeys $product @('module','role','sources','compiledIdentity')
        if ($product.module -cnotin $script:FcMergeInput.ModuleRoster -or -not $modules.Add([string] $product.module) -or
            $product.role -cne @($roster | Where-Object name -CEQ $product.module)[0].role -or $product.sources.Count -le 0 -or
            $product.compiledIdentity.compiledSourceBindingComplete -ne $true) { throw $script:FcMergeInput.InvalidSource }
        $identity = $product.compiledIdentity
        if ([IO.Path]::GetFullPath([string] $identity.inspectedSourceRoot) -cne [IO.Path]::GetFullPath($Repository)) {
            throw $script:FcMergeInput.InvalidSource
        }
        if ($identity.compileReceipt.producer.path -cne $producer.path -or
            $identity.compileReceipt.producer.sha256 -cne $producer.sha256) {
            throw $script:FcMergeInput.InvalidSource
        }
        $actualIdentity = Read-FcCompiledIdentity $Repository ([string] $identity.dll) ([string] $identity.pdb) `
            @($product.sources) ([string] $identity.originalCompilationRoot)
        $central = @($identity.compileReceipt.centralInputs)
        $expectedInputs = [string[]]@('global.json','Directory.Build.props','Directory.Build.targets',
            'Directory.Packages.props','KeyLoad.slnx',@($roster | Where-Object name -CEQ $product.module)[0].path)
        $centralPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($input in $central) {
            Assert-FcExactKeys $input @('path','sha256')
            if (-not $centralPaths.Add([string] $input.path) -or $input.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
                (Get-FcHash (Resolve-FcPath $Repository $input.path)) -cne $input.sha256) { throw $script:FcMergeInput.InvalidSource }
        }
        if ($central.Count -ne $expectedInputs.Count -or -not $centralPaths.SetEquals($expectedInputs)) {
            throw $script:FcMergeInput.InvalidSource
        }
        $actualIdentity['compileReceipt'] = Read-FcCompileReceipt (Resolve-FcPath $Repository $identity.dll) `
            @($product.sources) $central $currentProducer
        $actualJson = ConvertTo-Json -InputObject $actualIdentity -Depth 12 -Compress
        $expectedJson = ConvertTo-Json -InputObject $identity -Depth 12 -Compress
        if ($actualJson -cne $expectedJson) { throw $script:FcMergeInput.InvalidSource }
        $sourcePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($source in $product.sources) {
            Assert-FcExactKeys $source @('path','sha256')
            if ([string]::IsNullOrWhiteSpace($source.path) -or -not $sourcePaths.Add([string] $source.path)) {
                throw $script:FcMergeInput.InvalidSource
            }
            if ($source.sha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcMergeInput.InvalidSource }
            $sourcePath = Resolve-FcPath $Repository ([string] $source.path)
            if ((Get-FcHash $sourcePath) -cne $source.sha256) { throw $script:FcMergeInput.InvalidSource }
        }
    }
    if (-not $modules.SetEquals([string[]] $script:FcMergeInput.ModuleRoster)) { throw $script:FcMergeInput.InvalidSource }
    Assert-FcSourceScripts $Repository $Manifest.scripts
}

function Assert-FcCapturedMtpSettings([string] $EvidenceRoot, [object] $ManifestFile,
    [string] $ExpectedSha256, [object] $Bounds) {
    $manifestDirectory = [IO.Path]::GetDirectoryName([string] $ManifestFile.path)
    $relative = [IO.Path]::GetRelativePath($EvidenceRoot, $manifestDirectory).Replace([IO.Path]::DirectorySeparatorChar, '/')
    $settingsRelative = if ($relative -eq '.') { 'functional-coverage.production.settings.xml' } else {
        $relative + '/functional-coverage.production.settings.xml'
    }
    $settings = Resolve-FcEvidencePath $EvidenceRoot $settingsRelative $Bounds.maximumPathCharacters
    $actual = Read-FcHashFile $settings $Bounds.maximumFileBytes $Bounds.readBufferBytes $script:FcMergeInput.InvalidEvidence
    if ($actual.sha256 -cne $ExpectedSha256 -or $script:FcMergeInput.ReferenceCatalog.ContainsKey($settings)) {
        throw $script:FcMergeInput.InvalidEvidence
    }
    if ($script:FcMergeInput.ReferenceCatalog.Count -ge $Bounds.maximumFiles -or
        $script:FcMergeInput.ReferenceBytes -gt $Bounds.maximumTotalBytes - $actual.length) {
        throw $script:FcMergeInput.InvalidEvidence
    }
    $script:FcMergeInput.ReferenceCatalog.Add($settings, [ordered]@{ length = $actual.length; sha256 = $actual.sha256 })
    $script:FcMergeInput.ReferenceBytes += $actual.length
}
