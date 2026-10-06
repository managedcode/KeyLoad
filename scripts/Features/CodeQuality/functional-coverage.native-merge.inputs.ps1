$script:FcNativeMergeInput = [ordered]@{
    DescriptorVersion = 1
    SourceManifestVersion = 3
    ReadBufferBytes = 0
    MaximumFiles = 0
    MaximumTotalBytes = 0
    MaximumFileBytes = 0
    ReferenceCatalog = $null
    ReferenceBytes = 0L
    ScriptHashes = $null
    NativeToolVersion = '18.11.2'
    ToolPackageId = 'dotnet-coverage'
    Rf3NodeCount = 3
    MaximumJsonDepth = 32
    InvalidDescriptor = 'The native coverage merge descriptor is invalid.'
    InvalidEvidence = 'A native coverage merge input is absent, changed or outside its evidence root.'
    InvalidSource = 'The native coverage merge source identity is incomplete or stale.'
    InvalidReceipt = 'An original native coverage terminal receipt is incomplete or mismatched.'
    ModuleRoster = @('KeyLoad.Abstractions','KeyLoad.Analyzers','KeyLoad.AppHost','KeyLoad.Artifacts','KeyLoad.Cli',
        'KeyLoad.Client','KeyLoad.Core','KeyLoad.Diagnostics','KeyLoad.Orleans','KeyLoad.Query','KeyLoad.Replication',
        'KeyLoad.Security','KeyLoad.Server','KeyLoad.ServiceDefaults','KeyLoad.Storage.IO','KeyLoad.Storage.ZoneTree')
    ServerModuleRoster = @('KeyLoad.Abstractions','KeyLoad.Core','KeyLoad.Diagnostics','KeyLoad.Orleans','KeyLoad.Query',
        'KeyLoad.Replication','KeyLoad.Security','KeyLoad.Server','KeyLoad.ServiceDefaults','KeyLoad.Storage.IO','KeyLoad.Storage.ZoneTree')
}

function Assert-FcNativeExactKeys([Collections.IDictionary] $Value, [string[]] $Names) {
    if ($null -eq $Value -or $Value.Count -ne $Names.Count) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    foreach ($name in $Names) { if (-not $Value.Contains($name)) { throw $script:FcNativeMergeInput.InvalidDescriptor } }
}

function Assert-FcNativeJsonUnique([System.Text.Json.JsonElement] $Element, [int] $Depth = 0) {
    if ($Depth -gt $script:FcNativeMergeInput.MaximumJsonDepth) { throw $script:FcNativeMergeInput.InvalidDescriptor }
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw $script:FcNativeMergeInput.InvalidDescriptor }
            Assert-FcNativeJsonUnique $property.Value ($Depth + 1)
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($child in $Element.EnumerateArray()) { Assert-FcNativeJsonUnique $child ($Depth + 1) }
    }
}

function Read-FcNativeBoundedFile([string] $Path, [long] $MaximumBytes, [int] $ReadBufferBytes, [string] $Failure) {
    if (-not [IO.File]::Exists($Path)) { throw $Failure }
    $before = Get-Item -LiteralPath $Path -Force
    if ($before -isnot [IO.FileInfo] -or $before.Length -le 0 -or $before.Length -gt $MaximumBytes -or
        ($before.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw $Failure }
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $failure = $null
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
    catch [System.Exception] { $failure = $_.Exception }
    finally {
        try { $stream.Dispose() }
        catch [System.Exception] {
            if ($null -eq $failure) { $failure = $_.Exception }
            else { $failure = [AggregateException]::new($failure, $_.Exception) }
        }
    }
    if ($null -ne $failure) { throw $failure }
    [ordered]@{ bytes = $bytes; length = $before.Length; sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
}

function Assert-FcNativeNoReparsePath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $current = [IO.Path]::GetPathRoot($full)
    foreach ($segment in $full.Substring($current.Length).Split([char[]]@([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw $script:FcNativeMergeInput.InvalidEvidence }
        }
    }
}

function Resolve-FcNativeEvidencePath([string] $Root, [string] $Relative, [int] $MaximumCharacters) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative.Length -gt $MaximumCharacters -or
        [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains('\') -or
        $Relative.Split('/') -contains '..' -or $Relative.Split('/') -contains '.' -or
        $Relative -match '[\x00-\x1f\x7f]') { throw $script:FcNativeMergeInput.InvalidEvidence }
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, $Relative))
    $relativePath = [IO.Path]::GetRelativePath($Root, $full)
    if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath -eq '..' -or
        $relativePath.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw $script:FcNativeMergeInput.InvalidEvidence
    }
    $current = $Root
    foreach ($part in $relativePath.Split([IO.Path]::DirectorySeparatorChar)) {
        $current = Join-Path $current $part
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw $script:FcNativeMergeInput.InvalidEvidence
        }
    }
    $full
}

function Read-FcNativeEvidenceFile([string] $Root, [object] $Reference, [long] $MaximumBytes, [int] $MaximumPathCharacters) {
    Assert-FcNativeExactKeys $Reference @('path','length','sha256')
    if ($Reference.length -isnot [long] -and $Reference.length -isnot [int] -or
        $Reference.length -le 0 -or $Reference.length -gt $MaximumBytes -or
        $Reference.sha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeMergeInput.InvalidEvidence }
    $path = Resolve-FcNativeEvidencePath $Root ([string] $Reference.path) $MaximumPathCharacters
    $read = Read-FcNativeBoundedFile $path $MaximumBytes $script:FcNativeMergeInput.ReadBufferBytes $script:FcNativeMergeInput.InvalidEvidence
    if ($read.length -ne $Reference.length -or $read.sha256 -cne $Reference.sha256) { throw $script:FcNativeMergeInput.InvalidEvidence }
    if (-not $script:FcNativeMergeInput.ReferenceCatalog.ContainsKey($path)) {
        if ($script:FcNativeMergeInput.ReferenceCatalog.Count -ge $script:FcNativeMergeInput.MaximumFiles -or
            $script:FcNativeMergeInput.ReferenceBytes -gt $script:FcNativeMergeInput.MaximumTotalBytes - $read.length) {
            throw $script:FcNativeMergeInput.InvalidEvidence
        }
        $script:FcNativeMergeInput.ReferenceCatalog.Add($path, [ordered]@{ length = $read.length; sha256 = $read.sha256 })
        $script:FcNativeMergeInput.ReferenceBytes += $read.length
    }
    elseif ($script:FcNativeMergeInput.ReferenceCatalog[$path].length -ne $read.length -or
        $script:FcNativeMergeInput.ReferenceCatalog[$path].sha256 -cne $read.sha256) { throw $script:FcNativeMergeInput.InvalidEvidence }
    [ordered]@{ path = $path; relativePath = [string] $Reference.path; bytes = $read.bytes; length = $read.length; sha256 = $read.sha256 }
}

function Read-FcNativeJsonReference([string] $Root, [object] $Reference, [long] $MaximumBytes, [int] $MaximumPathCharacters) {
    $file = Read-FcNativeEvidenceFile $Root $Reference $MaximumBytes $MaximumPathCharacters
    try {
        $json = [System.Text.Json.JsonDocument]::Parse($file.bytes)
        Assert-FcNativeJsonUnique $json.RootElement
        $value = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($file.bytes)) -AsHashtable -Depth $script:FcNativeMergeInput.MaximumJsonDepth
        return [ordered]@{ file = $file; value = $value }
    }
    catch [System.Exception] { throw $script:FcNativeMergeInput.InvalidEvidence }
    finally { if ($null -ne $json) { $json.Dispose() } }
}

function Assert-FcNativeSourceManifest([string] $Repository, [object] $Manifest) {
    Assert-FcNativeExactKeys $Manifest @('schemaVersion','sourceRevision','repository','contractSha256',
        'compiledProducts','compiledTestsManifest','compilationProducer','contributors','settingsSha256','scripts')
    if ($Manifest.schemaVersion -ne $script:FcNativeMergeInput.SourceManifestVersion -or
        $Manifest.sourceRevision -cnotmatch '\A[0-9a-f]{40}\z' -or
        [IO.Path]::GetFullPath([string] $Manifest.repository) -cne [IO.Path]::GetFullPath($Repository) -or
        $Manifest.contractSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Manifest.settingsSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Manifest.compiledProducts.Count -ne $script:FcNativeMergeInput.ModuleRoster.Count) {
        throw $script:FcNativeMergeInput.InvalidSource
    }
    $producer = $Manifest.compilationProducer
    $currentProducer = Get-FcCompilationProducerBinding $Repository
    if ($producer -isnot [Collections.IDictionary] -or $producer.Count -ne 2 -or
        $producer.path -cne $currentProducer.path -or $producer.sha256 -cne $currentProducer.sha256) {
        throw $script:FcNativeMergeInput.InvalidSource
    }
    $modules = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($product in $Manifest.compiledProducts) {
        Assert-FcNativeExactKeys $product @('module','role','sources','compiledIdentity')
        if ($product.module -cnotin $script:FcNativeMergeInput.ModuleRoster -or -not $modules.Add([string] $product.module) -or
            $product.role -cne (if ($product.module -in @('KeyLoad.Analyzers','KeyLoad.AppHost')) { 'infrastructure' } else { 'production' }) -or $product.sources.Count -le 0 -or
            $product.compiledIdentity.compiledSourceBindingComplete -ne $true) { throw $script:FcNativeMergeInput.InvalidSource }
        $identity = $product.compiledIdentity
        if ($identity.compileReceipt.producer.path -cne $producer.path -or
            $identity.compileReceipt.producer.sha256 -cne $producer.sha256) {
            throw $script:FcNativeMergeInput.InvalidSource
        }
        $actualIdentity = Read-FcCompiledIdentity $Repository ([string] $identity.dll) ([string] $identity.pdb) `
            @($product.sources) ([string] $identity.originalCompilationRoot)
        $actualJson = ConvertTo-Json -InputObject $actualIdentity -Depth 12 -Compress
        $expectedJson = ConvertTo-Json -InputObject $identity -Depth 12 -Compress
        if ($actualJson -cne $expectedJson) { throw $script:FcNativeMergeInput.InvalidSource }
        $sourcePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($source in $product.sources) {
            Assert-FcNativeExactKeys $source @('path','sha256')
            if ([string]::IsNullOrWhiteSpace($source.path) -or -not $sourcePaths.Add([string] $source.path)) {
                throw $script:FcNativeMergeInput.InvalidSource
            }
            if ($source.sha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeMergeInput.InvalidSource }
            $sourcePath = Resolve-FcPath $Repository ([string] $source.path)
            if ((Get-FcHash $sourcePath) -cne $source.sha256) { throw $script:FcNativeMergeInput.InvalidSource }
        }
    }
    if (-not $modules.SetEquals([string[]] $script:FcNativeMergeInput.ModuleRoster)) { throw $script:FcNativeMergeInput.InvalidSource }
    Assert-FcNativeSourceScripts $Repository $Manifest.scripts
}

function Assert-FcNativeCapturedMtpSettings([string] $EvidenceRoot, [object] $ManifestFile,
    [string] $ExpectedSha256, [object] $Bounds) {
    $manifestDirectory = [IO.Path]::GetDirectoryName([string] $ManifestFile.path)
    $relative = [IO.Path]::GetRelativePath($EvidenceRoot, $manifestDirectory).Replace([IO.Path]::DirectorySeparatorChar, '/')
    $settingsRelative = if ($relative -eq '.') { 'functional-coverage.production.settings.xml' } else {
        $relative + '/functional-coverage.production.settings.xml'
    }
    $settings = Resolve-FcNativeEvidencePath $EvidenceRoot $settingsRelative $Bounds.maximumPathCharacters
    $actual = Read-FcNativeHashFile $settings $Bounds.maximumFileBytes $Bounds.readBufferBytes $script:FcNativeMergeInput.InvalidEvidence
    if ($actual.sha256 -cne $ExpectedSha256 -or $script:FcNativeMergeInput.ReferenceCatalog.ContainsKey($settings)) {
        throw $script:FcNativeMergeInput.InvalidEvidence
    }
    if ($script:FcNativeMergeInput.ReferenceCatalog.Count -ge $Bounds.maximumFiles -or
        $script:FcNativeMergeInput.ReferenceBytes -gt $Bounds.maximumTotalBytes - $actual.length) {
        throw $script:FcNativeMergeInput.InvalidEvidence
    }
    $script:FcNativeMergeInput.ReferenceCatalog.Add($settings, [ordered]@{ length = $actual.length; sha256 = $actual.sha256 })
    $script:FcNativeMergeInput.ReferenceBytes += $actual.length
}
