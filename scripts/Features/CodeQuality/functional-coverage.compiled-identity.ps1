$script:FcCompiledIdentity = [ordered]@{
    MaximumArtifactBytes = 67108864L
    MaximumDocuments = 5000
    MaximumPathCharacters = 4096
    Sha256Algorithm = [Guid] '8829d00f-11b8-4213-878b-770e8597ac16'
    InvalidArtifact = 'The compiled coverage identity is absent, oversized or unsupported.'
    InvalidPdb = 'The portable PDB does not belong to the inspected PE image.'
    InvalidDocument = 'A compiled source document is duplicated, unsupported or outside the owned checkout.'
    SourceDrift = 'Compiled source bytes do not match the current source inventory.'
    GeneratedDisposition = 'generated; retained native checksum, excluded from inventoried product-source binding'
    MissingDisposition = 'unmeasured; no portable-PDB document binds this source'
    UninventoriedDisposition = 'unmeasured; native document is outside the declared source inventory'
}
$script:FcCompiledIdentityToolPath = $PSCommandPath

function Resolve-FcCompiledFile([string] $Root, [string] $Relative) {
    if (-not [IO.Path]::IsPathFullyQualified($Root) -or -not [IO.Directory]::Exists($Root) -or
        (Get-Item -LiteralPath $Root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw $script:FcCompiledIdentity.InvalidArtifact
    }
    $path = Resolve-FcPath $Root $Relative
    if (-not [IO.File]::Exists($path)) { throw $script:FcCompiledIdentity.InvalidArtifact }
    $info = Get-Item -LiteralPath $path -Force
    if ($info -isnot [IO.FileInfo] -or $info.Length -le 0 -or
        $info.Length -gt $script:FcCompiledIdentity.MaximumArtifactBytes) {
        throw $script:FcCompiledIdentity.InvalidArtifact
    }
    $path
}

function Read-FcPeIdentity([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $pe = $null
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $module = $reader.GetModuleDefinition()
        $entries = @($pe.ReadDebugDirectory() | Where-Object Type -eq ([Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView))
        if ($entries.Count -ne 1) { throw $script:FcCompiledIdentity.InvalidPdb }
        $codeView = $pe.ReadCodeViewDebugDirectoryData($entries[0])
        if ($codeView.Age -ne 1) { throw $script:FcCompiledIdentity.InvalidPdb }
        [ordered]@{
            moduleName = $reader.GetString($module.Name)
            mvid = $reader.GetGuid($module.Mvid).ToString('D')
            pdbGuid = $codeView.Guid.ToString('D')
            pdbStamp = [uint32] $entries[0].Stamp
        }
    }
    finally {
        if ($null -ne $pe) { $pe.Dispose() }
        $stream.Dispose()
    }
}

function Assert-FcPdbDebugIdentity([object] $Reader, [object] $PeIdentity) {
    $identifier = [byte[]] $Reader.DebugMetadataHeader.Id
    if ($identifier.Length -ne 20 -or
        [Guid]::new([byte[]] $identifier[0..15]).ToString('D') -cne $PeIdentity.pdbGuid -or
        [BitConverter]::ToUInt32($identifier, 16) -ne $PeIdentity.pdbStamp) {
        throw $script:FcCompiledIdentity.InvalidPdb
    }
}

function Get-FcPdbSourcePath([string] $Root, [string] $Name, [string] $CompilationRoot) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Length -gt $script:FcCompiledIdentity.MaximumPathCharacters) {
        throw $script:FcCompiledIdentity.InvalidDocument
    }
    $foreignRooted = $Name -match '\A[A-Za-z]:[\\/]' -or $Name.StartsWith('\\', [StringComparison]::Ordinal)
    if ($foreignRooted -and -not [IO.Path]::IsPathRooted($Name)) {
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Name))).ToLowerInvariant()
        return "external/$digest"
    }
    $full = if ([IO.Path]::IsPathRooted($Name)) {
        [IO.Path]::GetFullPath($Name)
    }
    else {
        [IO.Path]::GetFullPath([IO.Path]::Combine($CompilationRoot, $Name))
    }
    $relative = [IO.Path]::GetRelativePath($CompilationRoot, $full)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
        $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        $relative.StartsWith('..' + [IO.Path]::AltDirectorySeparatorChar, [StringComparison]::Ordinal)) {
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($full))).ToLowerInvariant()
        return "external/$digest"
    }
    $resolved = Resolve-FcPath $Root $relative
    $relative.Replace([IO.Path]::DirectorySeparatorChar, '/')
}

function Read-FcPdbDocuments([string] $Root, [object] $Reader, [string] $CompilationRoot) {
    $documents = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($handle in $Reader.Documents) {
        if ($documents.Count -ge $script:FcCompiledIdentity.MaximumDocuments) {
            throw $script:FcCompiledIdentity.InvalidDocument
        }
        $document = $Reader.GetDocument($handle)
        $path = Get-FcPdbSourcePath $Root $Reader.GetString($document.Name) $CompilationRoot
        $checksum = [byte[]] $Reader.GetBlobBytes($document.Hash)
        if (-not $seen.Add($path) -or $Reader.GetGuid($document.HashAlgorithm) -ne $script:FcCompiledIdentity.Sha256Algorithm -or
            $checksum.Length -ne 32) { throw $script:FcCompiledIdentity.InvalidDocument }
        $external = $path.StartsWith('external/', [StringComparison]::Ordinal)
        $generated = $external -or $path.Split('/') -contains 'obj' -or $path.Split('/') -contains 'bin'
        $disposition = if ($external) { 'external PDB document; retained native checksum, path replaced by bounded digest' }
            elseif ($generated) { $script:FcCompiledIdentity.GeneratedDisposition }
            else { 'native source document' }
        $documents.Add([ordered]@{ path = $path; sha256 = [Convert]::ToHexString($checksum).ToLowerInvariant(); generated = $generated; disposition = $disposition })
    }
    @($documents | Sort-Object path)
}

function Read-FcPortablePdb([string] $Root, [string] $Path, [object] $PeIdentity, [string] $CompilationRoot) {
    $stream = [IO.File]::OpenRead($Path)
    $provider = $null
    try {
        $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream(
            $stream, [Reflection.Metadata.MetadataStreamOptions]::Default, 0)
        $reader = $provider.GetMetadataReader()
        Assert-FcPdbDebugIdentity $reader $PeIdentity
        Read-FcPdbDocuments $Root $reader $CompilationRoot
    }
    finally {
        if ($null -ne $provider) { $provider.Dispose() }
        $stream.Dispose()
    }
}

function Assert-FcCompiledSources([string] $Root, [object[]] $Sources, [object[]] $Documents) {
    if ($Sources.Count -eq 0 -or $Sources.Count -gt $script:FcCompiledIdentity.MaximumDocuments) {
        throw $script:FcCompiledIdentity.InvalidDocument
    }
    $expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($source in $Sources) {
        if ($expected.Count -ge $script:FcCompiledIdentity.MaximumDocuments -or $expected.ContainsKey([string] $source.path)) {
            throw $script:FcCompiledIdentity.InvalidDocument
        }
        $path = Resolve-FcPath $Root ([string] $source.path)
        if ((Get-FcHash $path) -cne $source.sha256) { throw $script:FcCompiledIdentity.SourceDrift }
        $expected.Add([string] $source.path, $source)
    }
    $matched = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($document in $Documents) {
        if ($document.generated) { continue }
        if (-not $expected.ContainsKey([string] $document.path)) {
            $document.disposition = $script:FcCompiledIdentity.UninventoriedDisposition
            continue
        }
        if ($document.sha256 -cne $expected[[string] $document.path].sha256) {
            throw $script:FcCompiledIdentity.SourceDrift
        }
        [void] $matched.Add([string] $document.path)
    }
    @($expected.Keys | Where-Object { -not $matched.Contains($_) } | Sort-Object | ForEach-Object {
        [ordered]@{ path = $_; disposition = $script:FcCompiledIdentity.MissingDisposition }
    })
}

function Resolve-FcCompilationRoot([string] $Root, [string] $OriginalCompilationRoot) {
    $selected = if ([string]::IsNullOrEmpty($OriginalCompilationRoot)) { $Root } else { $OriginalCompilationRoot }
    if ([string]::IsNullOrWhiteSpace($selected) -or
        $selected.Length -gt $script:FcCompiledIdentity.MaximumPathCharacters -or
        -not [IO.Path]::IsPathFullyQualified($selected)) { throw $script:FcCompiledIdentity.InvalidDocument }
    [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($selected))
}

function Read-FcCompiledIdentity([string] $Root, [string] $Dll, [string] $Pdb, [object[]] $Sources, [string] $OriginalCompilationRoot = '') {
    $dllPath = Resolve-FcCompiledFile $Root $Dll
    $pdbPath = Resolve-FcCompiledFile $Root $Pdb
    $compilationRoot = Resolve-FcCompilationRoot $Root $OriginalCompilationRoot
    $toolBefore = Get-FcHash $script:FcCompiledIdentityToolPath
    $beforeDll = Get-FcHash $dllPath
    $beforePdb = Get-FcHash $pdbPath
    $identity = Read-FcPeIdentity $dllPath
    if ($identity.moduleName -cne [IO.Path]::GetFileName($dllPath)) { throw $script:FcCompiledIdentity.InvalidArtifact }
    $documents = @(Read-FcPortablePdb $Root $pdbPath $identity $compilationRoot)
    $missing = @(Assert-FcCompiledSources $Root $Sources $documents)
    if ((Get-FcHash $dllPath) -cne $beforeDll -or (Get-FcHash $pdbPath) -cne $beforePdb -or
        (Get-FcHash $script:FcCompiledIdentityToolPath) -cne $toolBefore) {
        throw $script:FcCompiledIdentity.SourceDrift
    }
    foreach ($source in $Sources) {
        if ((Get-FcHash (Resolve-FcPath $Root ([string] $source.path))) -cne $source.sha256) {
            throw $script:FcCompiledIdentity.SourceDrift
        }
    }
    [ordered]@{
        dll = $Dll; pdb = $Pdb; dllSha256 = $beforeDll; pdbSha256 = $beforePdb
        moduleName = $identity.moduleName; compiledIdentityToolSha256 = $toolBefore
        originalCompilationRoot = $compilationRoot; inspectedSourceRoot = [IO.Path]::GetFullPath($Root)
        mvid = $identity.mvid; pdbGuid = $identity.pdbGuid; pdbStamp = $identity.pdbStamp
        documents = $documents; sourceFilesWithoutPdbDocuments = $missing
        compiledSourceBindingComplete = $missing.Count -eq 0
        qualification = 'static native identity inspection only; no test, coverage or build qualification'
    }
}
