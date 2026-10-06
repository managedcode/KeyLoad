$script:FcTestIdentity = [ordered]@{
    SchemaVersion = 1
    ProjectDirectory = 'tests/KeyLoad.UnitTests'
    ProjectFile = 'tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj'
    CompilationProducer = 'tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets'
    DeploymentDirectory = 'tests/KeyLoad.UnitTests/bin/Release/net10.0'
    Dll = 'tests/KeyLoad.UnitTests/bin/Release/net10.0/KeyLoad.UnitTests.dll'
    Pdb = 'tests/KeyLoad.UnitTests/bin/Release/net10.0/KeyLoad.UnitTests.pdb'
    MaximumEntries = 5000
    MaximumSources = 5000
    MaximumManifestBytes = 33554432L
    MaximumPathCharacters = 4096
    MaximumJsonDepth = 12
    MaximumJsonNodes = 70000
    JsonNodeCount = 0
    CentralInputs = @(
        'global.json',
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props',
        'KeyLoad.slnx',
        'tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj'
    )
    InvalidRoot = 'The UnitTests identity root is absent, linked or unsupported.'
    InvalidInventory = 'The UnitTests project inventory is unsafe, oversized or ambiguous.'
    InvalidBuildInput = 'A required UnitTests build input is absent or unsafe.'
    InvalidImage = 'The actual Release UnitTests image is absent, oversized or unsupported.'
    IncompleteBinding = 'The UnitTests portable PDB does not bind every inventoried project source.'
    InvalidManifest = 'The UnitTests image identity manifest is malformed, stale or unsupported.'
    Drift = 'The UnitTests source, build inputs or compiled image changed during identity capture.'
    Qualification = 'static native UnitTests image identity only; no build, test or coverage qualification'
}

function Resolve-FcTestIdentityRoot([string] $Root) {
    if (-not [IO.Path]::IsPathFullyQualified($Root) -or -not [IO.Directory]::Exists($Root)) {
        throw $script:FcTestIdentity.InvalidRoot
    }
    $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
    $item = Get-Item -LiteralPath $full -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $script:FcTestIdentity.InvalidRoot }
    $full
}

function Test-FcTestGeneratedPath([string] $RelativePath) {
    $segments = $RelativePath.Split('/')
    return ($segments -contains 'obj' -or $segments -contains 'bin' -or
        $RelativePath.StartsWith('external/', [StringComparison]::Ordinal))
}

function Get-FcTestProjectSources([string] $Root) {
    $rootPath = Resolve-FcTestIdentityRoot $Root
    $projectPath = Resolve-FcPath $rootPath $script:FcTestIdentity.ProjectDirectory
    if (-not [IO.Directory]::Exists($projectPath) -or
        ((Get-Item -LiteralPath $projectPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw $script:FcTestIdentity.InvalidInventory
    }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($projectPath)
    $paths = [Collections.Generic.List[string]]::new()
    $visitedEntries = 0
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entryPath in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            if ($visitedEntries -ge $script:FcTestIdentity.MaximumEntries) { throw $script:FcTestIdentity.InvalidInventory }
            $visitedEntries++
            $entry = Get-Item -LiteralPath $entryPath -Force
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $script:FcTestIdentity.InvalidInventory }
            if ($entry -is [IO.DirectoryInfo]) {
                if ($entry.Name -cne 'obj' -and $entry.Name -cne 'bin') { $pending.Push($entry.FullName) }
                continue
            }
            if ($entry -isnot [IO.FileInfo] -or $entry.Extension -cne '.cs') { continue }
            $relative = [IO.Path]::GetRelativePath($rootPath, $entry.FullName).Replace([IO.Path]::DirectorySeparatorChar, '/')
            if (-not $relative.StartsWith($script:FcTestIdentity.ProjectDirectory + '/', [StringComparison]::Ordinal) -or
                (Test-FcTestGeneratedPath $relative)) { continue }
            if ($paths.Count -ge $script:FcTestIdentity.MaximumSources) { throw $script:FcTestIdentity.InvalidInventory }
            [void] $paths.Add($relative)
        }
    }
    if ($paths.Count -eq 0) { throw $script:FcTestIdentity.InvalidInventory }
    $orderedPaths = [string[]] $paths.ToArray()
    [Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sources = [Collections.Generic.List[object]]::new()
    foreach ($relative in $orderedPaths) {
        if (-not $seen.Add($relative)) { throw $script:FcTestIdentity.InvalidInventory }
        $path = Resolve-FcPath $rootPath $relative
        if (-not [IO.File]::Exists($path)) { throw $script:FcTestIdentity.InvalidInventory }
        $sources.Add([ordered]@{ path = $relative; sha256 = Get-FcHash $path })
    }
    @($sources.ToArray())
}

function Get-FcTestBuildInputs([string] $Root) {
    $rootPath = Resolve-FcTestIdentityRoot $Root
    $inputs = [Collections.Generic.List[object]]::new()
    foreach ($relative in $script:FcTestIdentity.CentralInputs) {
        $path = Resolve-FcPath $rootPath $relative
        if (-not [IO.File]::Exists($path)) { throw $script:FcTestIdentity.InvalidBuildInput }
        $inputs.Add([ordered]@{ path = $relative; sha256 = Get-FcHash $path })
    }
    @($inputs.ToArray())
}

function Assert-FcTestPdbDocumentSet([object[]] $Sources, [object[]] $Documents) {
    $sourcePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Sources) { [void] $sourcePaths.Add([string] $source.path) }
    $projectPrefix = $script:FcTestIdentity.ProjectDirectory + '/'
    foreach ($document in $Documents) {
        $path = [string] $document.path
        if ($document.generated) { continue }
        if (-not $path.StartsWith($projectPrefix, [StringComparison]::Ordinal) -or -not $sourcePaths.Contains($path)) {
            throw $script:FcTestIdentity.IncompleteBinding
        }
    }
}

function Get-FcTestCompiledIdentity([string] $Root, [object[]] $Sources, [object[]] $BuildInputs) {
    $identity = Read-FcCompiledIdentity $Root $script:FcTestIdentity.Dll $script:FcTestIdentity.Pdb $Sources
    if ($identity.moduleName -cne 'KeyLoad.UnitTests.dll') { throw $script:FcTestIdentity.IncompleteBinding }
    Assert-FcTestPdbDocumentSet $Sources @($identity.documents)
    $beforeDll = Get-FcHash (Resolve-FcPath $Root $script:FcTestIdentity.Dll)
    $beforePdb = Get-FcHash (Resolve-FcPath $Root $script:FcTestIdentity.Pdb)
    if ($beforeDll -cne $identity.dllSha256 -or $beforePdb -cne $identity.pdbSha256) {
        throw $script:FcTestIdentity.Drift
    }
    $producerPath = Resolve-FcPath $Root $script:FcTestIdentity.CompilationProducer
    $producer = [ordered]@{ path = $script:FcTestIdentity.CompilationProducer; sha256 = Get-FcHash $producerPath }
    $identity['compileReceipt'] = Read-FcUnitCompileReceipt (
        Resolve-FcPath $Root $script:FcTestIdentity.Dll) $Sources $BuildInputs $producer
    if ((Get-FcHash (Resolve-FcPath $Root $script:FcTestIdentity.Dll)) -cne $beforeDll -or
        (Get-FcHash (Resolve-FcPath $Root $script:FcTestIdentity.Pdb)) -cne $beforePdb) {
        throw $script:FcTestIdentity.Drift
    }
    $identity
}

function Get-FcTestIdentitySnapshot([string] $Root) {
    $rootPath = Resolve-FcTestIdentityRoot $Root
    $beforeInputs = @(Get-FcTestBuildInputs $rootPath)
    $sources = @(Get-FcTestProjectSources $rootPath)
    $producerPath = Resolve-FcPath $rootPath $script:FcTestIdentity.CompilationProducer
    $beforeProducer = Get-FcHash $producerPath
    $compiled = Get-FcTestCompiledIdentity $rootPath $sources $beforeInputs
    $afterProducer = Get-FcHash $producerPath
    $afterInputs = @(Get-FcTestBuildInputs $rootPath)
    $afterSources = @(Get-FcTestProjectSources $rootPath)
    if ($beforeProducer -cne $afterProducer -or
        (ConvertTo-Json -InputObject $beforeInputs -Compress) -cne (ConvertTo-Json -InputObject $afterInputs -Compress) -or
        (ConvertTo-Json -InputObject $sources -Compress) -cne (ConvertTo-Json -InputObject $afterSources -Compress)) {
        throw $script:FcTestIdentity.Drift
    }
    [ordered]@{
        schemaVersion = $script:FcTestIdentity.SchemaVersion
        project = $script:FcTestIdentity.ProjectFile
        sources = $sources
        buildInputs = $beforeInputs
        compiledIdentity = $compiled
        qualification = $script:FcTestIdentity.Qualification
    }
}

function Assert-FcTestJsonObjectKeys([System.Text.Json.JsonElement] $Element, [string[]] $Expected) {
    if ($Element.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw $script:FcTestIdentity.InvalidManifest }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($property in $Element.EnumerateObject()) {
        if (-not $seen.Add($property.Name)) { throw $script:FcTestIdentity.InvalidManifest }
    }
    if ($seen.Count -ne $Expected.Count) { throw $script:FcTestIdentity.InvalidManifest }
    foreach ($name in $Expected) {
        if (-not $seen.Contains($name)) { throw $script:FcTestIdentity.InvalidManifest }
    }
}

function Assert-FcTestNoDuplicateJsonProperties([System.Text.Json.JsonElement] $Element, [int] $Depth = 0) {
    if ($Depth -gt $script:FcTestIdentity.MaximumJsonDepth) { throw $script:FcTestIdentity.InvalidManifest }
    $script:FcTestIdentity['JsonNodeCount'] = $script:FcTestIdentity['JsonNodeCount'] + 1
    if ($script:FcTestIdentity.JsonNodeCount -gt $script:FcTestIdentity.MaximumJsonNodes) { throw $script:FcTestIdentity.InvalidManifest }
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $seen.Add($property.Name)) { throw $script:FcTestIdentity.InvalidManifest }
            Assert-FcTestNoDuplicateJsonProperties $property.Value ($Depth + 1)
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-FcTestNoDuplicateJsonProperties $item ($Depth + 1) }
    }
}

function Read-FcBoundedTestManifestBytes([string] $Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $memory = [IO.MemoryStream]::new()
    $buffer = [byte[]]::new(8192)
    try {
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($memory.Length -gt ($script:FcTestIdentity.MaximumManifestBytes - $read)) {
                throw $script:FcTestIdentity.InvalidManifest
            }
            $memory.Write($buffer, 0, $read)
        }
        if ($memory.Length -eq 0) { throw $script:FcTestIdentity.InvalidManifest }
        ,$memory.ToArray()
    }
    finally { $memory.Dispose(); $stream.Dispose() }
}

function Assert-FcTestManifestEntries([System.Text.Json.JsonElement] $Element, [string] $Field, [object[]] $ExpectedEntries) {
    $entriesElement = $Element.GetProperty($Field)
    if ($entriesElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or
        $entriesElement.GetArrayLength() -ne $ExpectedEntries.Count) { throw $script:FcTestIdentity.InvalidManifest }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $index = 0
    foreach ($entry in $entriesElement.EnumerateArray()) {
        Assert-FcTestJsonObjectKeys $entry @('path','sha256')
        if ($entry.GetProperty('path').ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
            $entry.GetProperty('sha256').ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
            throw $script:FcTestIdentity.InvalidManifest
        }
        $path = $entry.GetProperty('path').GetString()
        $hash = $entry.GetProperty('sha256').GetString()
        if ($null -eq $path -or $null -eq $hash -or -not $seen.Add($path) -or
            $hash -cnotmatch '\A[0-9a-f]{64}\z' -or $path -cne $ExpectedEntries[$index].path -or
            $hash -cne $ExpectedEntries[$index].sha256) {
            throw $script:FcTestIdentity.InvalidManifest
        }
        $index++
    }
}

function Assert-FcUnitCompileReceiptManifest([System.Text.Json.JsonElement] $Element, [object] $Expected) {
    Assert-FcTestJsonObjectKeys $Element @('version','sourceCount','sourceSetSha256','centralInputCount','centralInputs','producer','binding')
    if ($Element.GetProperty('version').ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
        $Element.GetProperty('version').GetInt32() -ne 1 -or
        $Element.GetProperty('sourceCount').ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
        $Element.GetProperty('sourceCount').GetInt32() -ne $Expected.sourceCount -or
        $Element.GetProperty('centralInputCount').ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
        $Element.GetProperty('centralInputCount').GetInt32() -ne 6 -or
        $Element.GetProperty('sourceSetSha256').ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
        $Element.GetProperty('sourceSetSha256').GetString() -cne $Expected.sourceSetSha256 -or
        $Element.GetProperty('sourceSetSha256').GetString() -cnotmatch '\A[0-9a-f]{64}\z' -or
        $Element.GetProperty('binding').ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
        $Element.GetProperty('binding').GetString() -cne $Expected.binding) {
        throw $script:FcTestIdentity.InvalidManifest
    }
    Assert-FcTestManifestEntries $Element 'centralInputs' @($Expected.centralInputs)
    Assert-FcTestJsonObjectKeys $Element.GetProperty('producer') @('path','sha256')
    if ($Element.GetProperty('producer').GetProperty('path').GetString() -cne $Expected.producer.path -or
        $Element.GetProperty('producer').GetProperty('sha256').GetString() -cne $Expected.producer.sha256) {
        throw $script:FcTestIdentity.InvalidManifest
    }
}

function Assert-FcTestCompiledManifest([System.Text.Json.JsonElement] $Element, [string[]] $SourcePaths, [object] $ExpectedIdentity) {
    $fields = @('dll','pdb','dllSha256','pdbSha256','moduleName','compiledIdentityToolSha256',
        'originalCompilationRoot','inspectedSourceRoot','mvid','pdbGuid','pdbStamp','documents',
        'sourceFilesWithoutPdbDocuments','compiledSourceBindingComplete','compileReceipt','qualification')
    Assert-FcTestJsonObjectKeys $Element $fields
    foreach ($name in @('dll','pdb','moduleName','compiledIdentityToolSha256','originalCompilationRoot',
            'inspectedSourceRoot','mvid','pdbGuid','qualification')) {
        if ($Element.GetProperty($name).ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
            throw $script:FcTestIdentity.InvalidManifest
        }
    }
    foreach ($name in @('dllSha256','pdbSha256','compiledIdentityToolSha256')) {
        if ($Element.GetProperty($name).GetString() -cnotmatch '\A[0-9a-f]{64}\z') {
            throw $script:FcTestIdentity.InvalidManifest
        }
    }
    $stampElement = $Element.GetProperty('pdbStamp')
    $stamp = [uint32] 0
    if ($stampElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
        -not $stampElement.TryGetUInt32([ref] $stamp)) { throw $script:FcTestIdentity.InvalidManifest }
    if ($Element.GetProperty('dll').GetString() -cne $script:FcTestIdentity.Dll -or
        $Element.GetProperty('pdb').GetString() -cne $script:FcTestIdentity.Pdb -or
        $Element.GetProperty('moduleName').GetString() -cne 'KeyLoad.UnitTests.dll' -or
        $Element.GetProperty('qualification').GetString() -cne $ExpectedIdentity.qualification -or
        $Element.GetProperty('dllSha256').GetString() -cne $ExpectedIdentity.dllSha256 -or
        $Element.GetProperty('pdbSha256').GetString() -cne $ExpectedIdentity.pdbSha256 -or
        $Element.GetProperty('compiledIdentityToolSha256').GetString() -cne $ExpectedIdentity.compiledIdentityToolSha256 -or
        $Element.GetProperty('moduleName').GetString() -cne $ExpectedIdentity.moduleName -or
        $Element.GetProperty('mvid').GetString() -cne $ExpectedIdentity.mvid -or
        $Element.GetProperty('pdbGuid').GetString() -cne $ExpectedIdentity.pdbGuid -or
        $Element.GetProperty('originalCompilationRoot').GetString() -cne $ExpectedIdentity.originalCompilationRoot -or
        $Element.GetProperty('inspectedSourceRoot').GetString() -cne $ExpectedIdentity.inspectedSourceRoot -or
        $stamp -ne [uint32] $ExpectedIdentity.pdbStamp -or
        $Element.GetProperty('compiledSourceBindingComplete').ValueKind -notin @([System.Text.Json.JsonValueKind]::True,[System.Text.Json.JsonValueKind]::False) -or
        $Element.GetProperty('compiledSourceBindingComplete').GetBoolean() -ne [bool] $ExpectedIdentity.compiledSourceBindingComplete -or
        $Element.GetProperty('mvid').GetString() -cnotmatch '\A[0-9a-fA-F-]{36}\z' -or
        $Element.GetProperty('pdbGuid').GetString() -cnotmatch '\A[0-9a-fA-F-]{36}\z') {
        throw $script:FcTestIdentity.InvalidManifest
    }
    Assert-FcTestPdbManifestDocuments $Element.GetProperty('documents') $SourcePaths @($ExpectedIdentity.documents)
    $missing = $Element.GetProperty('sourceFilesWithoutPdbDocuments')
    $expectedMissing = @($ExpectedIdentity.sourceFilesWithoutPdbDocuments)
    if ($missing.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or
        $missing.GetArrayLength() -ne $expectedMissing.Count) { throw $script:FcTestIdentity.InvalidManifest }
    for ($index = 0; $index -lt $expectedMissing.Count; $index++) {
        $item = $missing[$index]
        Assert-FcTestJsonObjectKeys $item @('path','disposition')
        if ($item.GetProperty('path').GetString() -cne $expectedMissing[$index].path -or
            $item.GetProperty('disposition').GetString() -cne $expectedMissing[$index].disposition) {
            throw $script:FcTestIdentity.InvalidManifest
        }
    }
    Assert-FcUnitCompileReceiptManifest $Element.GetProperty('compileReceipt') $ExpectedIdentity.compileReceipt
}

function Assert-FcTestPdbManifestDocuments([System.Text.Json.JsonElement] $Element, [string[]] $SourcePaths, [object[]] $ExpectedDocuments) {
    if ($Element.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or
        $Element.GetArrayLength() -gt $script:FcTestIdentity.MaximumSources -or
        $Element.GetArrayLength() -ne $ExpectedDocuments.Count) { throw $script:FcTestIdentity.InvalidManifest }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $SourcePaths) { [void] $expected.Add($source) }
    $seenDocuments = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $bound = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($document in $ExpectedDocuments) { $expectedByPath.Add([string] $document.path, $document) }
    foreach ($document in $Element.EnumerateArray()) {
        Assert-FcTestJsonObjectKeys $document @('path','sha256','generated','disposition')
        if ($document.GetProperty('path').ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
            $document.GetProperty('sha256').ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
            $document.GetProperty('generated').ValueKind -notin @([System.Text.Json.JsonValueKind]::True,[System.Text.Json.JsonValueKind]::False) -or
            $document.GetProperty('disposition').ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
            throw $script:FcTestIdentity.InvalidManifest
        }
        $path = $document.GetProperty('path').GetString()
        $hash = $document.GetProperty('sha256').GetString()
        $generated = $document.GetProperty('generated').GetBoolean()
        if ($null -eq $path -or $path.Length -gt $script:FcTestIdentity.MaximumPathCharacters -or
            -not $seenDocuments.Add($path) -or $null -eq $hash -or $hash -cnotmatch '\A[0-9a-f]{64}\z' -or
            (Test-FcTestGeneratedPath $path) -ne $generated -or -not $expectedByPath.ContainsKey($path) -or
            $hash -cne $expectedByPath[$path].sha256 -or $generated -ne [bool] $expectedByPath[$path].generated -or
            $document.GetProperty('disposition').GetString() -cne $expectedByPath[$path].disposition) {
            throw $script:FcTestIdentity.InvalidManifest
        }
        if (-not $generated) {
            if (-not $path.StartsWith($script:FcTestIdentity.ProjectDirectory + '/', [StringComparison]::Ordinal) -or
                -not $expected.Contains($path) -or -not $bound.Add($path)) { throw $script:FcTestIdentity.InvalidManifest }
        }
    }
    if ($bound.Count -eq 0) { throw $script:FcTestIdentity.InvalidManifest }
}

function Resolve-FcTestIdentityManifestPath([string] $Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path) -or $Path.Length -gt $script:FcTestIdentity.MaximumPathCharacters) {
        throw $script:FcTestIdentity.InvalidManifest
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $current = [IO.Path]::GetPathRoot($fullPath)
    $relative = $fullPath.Substring($current.Length)
    foreach ($segment in $relative.Split([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw $script:FcTestIdentity.InvalidManifest
        }
    }
    $fullPath
}

function Read-FcTestIdentityManifest([string] $Path, [string] $Root) {
    $fullPath = Resolve-FcTestIdentityManifestPath $Path
    if (-not [IO.File]::Exists($fullPath)) { throw $script:FcTestIdentity.InvalidManifest }
    $file = Get-Item -LiteralPath $fullPath -Force
    if ($file -isnot [IO.FileInfo] -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $file.Length -le 0 -or $file.Length -gt $script:FcTestIdentity.MaximumManifestBytes) {
        throw $script:FcTestIdentity.InvalidManifest
    }
    $rootPath = Resolve-FcTestIdentityRoot $Root
    [byte[]] $bytes = Read-FcBoundedTestManifestBytes $fullPath
    $jsonOptions = [System.Text.Json.JsonDocumentOptions]::new()
    $jsonOptions.MaxDepth = $script:FcTestIdentity.MaximumJsonDepth
    $document = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($bytes), $jsonOptions)
    try {
        $element = $document.RootElement
        $script:FcTestIdentity['JsonNodeCount'] = 0
        Assert-FcTestNoDuplicateJsonProperties $element
        Assert-FcTestJsonObjectKeys $element @('schemaVersion','project','sources','buildInputs','compiledIdentity','qualification')
        if ($element.GetProperty('schemaVersion').ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
            $element.GetProperty('project').ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
            $element.GetProperty('qualification').ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
            throw $script:FcTestIdentity.InvalidManifest
        }
        if ($element.GetProperty('schemaVersion').GetInt32() -ne $script:FcTestIdentity.SchemaVersion -or
            $element.GetProperty('project').GetString() -cne $script:FcTestIdentity.ProjectFile -or
            $element.GetProperty('qualification').GetString() -cne $script:FcTestIdentity.Qualification) {
            throw $script:FcTestIdentity.InvalidManifest
        }
        $snapshot = Get-FcTestIdentitySnapshot $rootPath
        $sources = @($snapshot.sources)
        $buildInputs = @($snapshot.buildInputs)
        Assert-FcTestManifestEntries $element 'sources' $sources
        Assert-FcTestManifestEntries $element 'buildInputs' $buildInputs
        $compiled = $snapshot.compiledIdentity
        Assert-FcTestCompiledManifest $element.GetProperty('compiledIdentity') ([string[]] @($sources | ForEach-Object { $_.path })) $compiled
        $manifest = $element.GetRawText() | ConvertFrom-Json -AsHashtable -Depth $script:FcTestIdentity.MaximumJsonDepth
        [ordered]@{ manifest = $manifest; sources = $sources; buildInputs = $buildInputs; compiledIdentity = $compiled }
    }
    finally { $document.Dispose() }
}
