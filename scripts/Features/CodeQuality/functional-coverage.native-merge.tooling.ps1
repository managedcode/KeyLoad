function Invoke-FcNativeToolingProof {
    Assert-FcNativeToolingArguments
    $ToolPackageRoot = [IO.Path]::GetFullPath($ToolPackageRoot)
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
    Assert-FcNativeNoReparsePath $ToolPackageRoot
    Assert-FcNativeNoReparsePath $EvidenceRoot
    Assert-FcNativeNoReparsePath $OutputDirectory
    Assert-FcNativeToolingOutputPath $EvidenceRoot $OutputDirectory
    $inputs = Get-FcNativeToolingInputs
    $script:FcNativeMergeInput.ActivePlan = $null
    $script:FcNativeMergeInput.ActiveToolingInputs = $inputs
    [void] [IO.Directory]::CreateDirectory($OutputDirectory)
    $counts = Invoke-FcNativeToolingMerges $inputs
    Assert-FcNativeToolingInputsUnchanged $inputs
    Write-FcNativeToolingProof $inputs $counts
}

function Assert-FcNativeToolingArguments {
    if (-not [IO.Path]::IsPathFullyQualified($ToolPackageRoot) -or -not [IO.Path]::IsPathFullyQualified($OutputDirectory) -or
        -not [IO.Path]::IsPathFullyQualified($ToolingInputDescriptor) -or
        $MaximumDescriptorBytes -le 0 -or $MaximumFiles -lt 4 -or
        $TimeoutSeconds -le 0 -or $ReadBufferBytes -le 0 -or $MaximumTotalBytes -le 0 -or
        $MaximumFileBytes -le 0 -or $MaximumPathCharacters -le 0 -or $MaximumManifestBytes -le 0 -or
        $MaximumReportBytes -le 0 -or $MaximumOutputCharacters -le 0 -or $SettlementTimeoutSeconds -le 0) {
        throw 'The native coverage tooling proof arguments are invalid.'
    }
    $script:FcNativeMergeInput.MaximumFiles = $MaximumFiles
    $script:FcNativeMergeInput.MaximumTotalBytes = $MaximumTotalBytes
    $script:FcNativeMergeInput.MaximumFileBytes = $MaximumFileBytes
    $script:FcNativeMergeInput.ReadBufferBytes = $ReadBufferBytes
    $script:FcNativeMergeInput.SettlementTimeoutSeconds = $SettlementTimeoutSeconds
}

function Assert-FcNativeToolingOutputPath([string] $Root, [string] $Output) {
    $relative = [IO.Path]::GetRelativePath($Root, $Output)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
        $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'The native coverage tooling proof output is outside its owned evidence root.'
    }
    if (Test-Path -LiteralPath $Output) { throw 'The native coverage tooling proof output already exists.' }
}

function Get-FcNativeToolingInputs {
    $inputs = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $total = 0L
    $descriptorPath = [IO.Path]::GetFullPath($ToolingInputDescriptor)
    $descriptorRelative = [IO.Path]::GetRelativePath($EvidenceRoot, $descriptorPath).Replace([IO.Path]::DirectorySeparatorChar, '/')
    if ([IO.Path]::IsPathRooted($descriptorRelative) -or $descriptorRelative -eq '..' -or
        $descriptorRelative.StartsWith('../', [StringComparison]::Ordinal) -or
        (Resolve-FcNativeEvidencePath $EvidenceRoot $descriptorRelative $MaximumPathCharacters) -cne $descriptorPath) {
        throw 'The native coverage tooling descriptor is outside its evidence root.'
    }
    Assert-FcNativeNoReparsePath $descriptorPath
    $descriptor = Read-FcNativeBoundedFile $descriptorPath $MaximumDescriptorBytes $ReadBufferBytes 'The native coverage tooling descriptor is invalid.'
    $json = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($descriptor.bytes))
    try {
        Assert-FcNativeJsonUnique $json.RootElement
        $value = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($descriptor.bytes)) -AsHashtable -Depth 8
    }
    finally { $json.Dispose() }
    Assert-FcNativeExactKeys $value @('schemaVersion','inputs')
    if ($value.schemaVersion -ne 1 -or $value.inputs -isnot [array] -or $value.inputs.Count -ne 3) {
        throw 'The native coverage tooling descriptor is invalid.'
    }
    foreach ($input in $value.inputs) {
        Assert-FcNativeExactKeys $input @('path','length','sha256')
        if (($input.length -isnot [int] -and $input.length -isnot [long]) -or $input.length -le 0 -or
            $input.length -gt $MaximumReportBytes -or $input.sha256 -cnotmatch '\A[0-9a-f]{64}\z') {
            throw 'The native coverage tooling descriptor is invalid.'
        }
        $path = Resolve-FcNativeEvidencePath $EvidenceRoot ([string] $input.path) $MaximumPathCharacters
        if (-not $seen.Add($path)) { throw 'The native coverage tooling input is duplicated.' }
        $read = Read-FcNativeBoundedFile $path $MaximumReportBytes $ReadBufferBytes 'The native coverage tooling input is invalid.'
        if ($read.length -ne $input.length -or $read.sha256 -cne $input.sha256) { throw 'The native coverage tooling input changed.' }
        if ($total -gt $MaximumTotalBytes - $read.length) { throw 'The native coverage tooling proof input bytes exceed their bound.' }
        $total += $read.length
        $inputs.Add([ordered]@{ path = $path; length = $read.length; sha256 = $read.sha256 })
    }
    if ($inputs.Count + 1 -gt $MaximumFiles) { throw 'The native coverage tooling input count exceeds its bound.' }
    $script:FcNativeToolingDescriptor = [ordered]@{ path = $descriptorPath; length = $descriptor.length; sha256 = $descriptor.sha256 }
    $inputs.ToArray()
}

function Invoke-FcNativeToolingMerges([object[]] $Inputs) {
    $paths = @($Inputs | ForEach-Object { $_.path })
    Export-FcNativeToolingInputs $paths
    $merged = Join-Path $OutputDirectory 'merged.coverage'
    Invoke-FcNativeMergeLogged $ToolPackageRoot $paths $OutputDirectory 'merged.coverage' 'coverage' `
        $TimeoutSeconds $MaximumOutputCharacters $MaximumFileBytes
    Export-FcNativeToolingMerged $merged
    $twice = [Collections.Generic.List[string]]::new()
    foreach ($path in $paths) { $twice.Add($path) }
    foreach ($path in $paths) { $twice.Add($path) }
    $control = Join-Path $OutputDirectory 'repeated-input-control.coverage'
    Invoke-FcNativeMergeLogged $ToolPackageRoot $twice.ToArray() $OutputDirectory 'repeated-input-control.coverage' 'coverage' `
        $TimeoutSeconds $MaximumOutputCharacters $MaximumFileBytes
    Export-FcNativeToolingControl $control
    $mergedCounts = Read-FcNativeMergeCoverage (Join-Path $OutputDirectory 'merged.cobertura') $MaximumFileBytes
    $controlCounts = Read-FcNativeMergeCoverage (Join-Path $OutputDirectory 'repeated-input-control.cobertura') $MaximumFileBytes
    Assert-FcNativeMergeCountsEqual $mergedCounts $controlCounts
    Assert-FcNativeToolingLineUnion $Inputs.Count $mergedCounts
    $mergedCounts
}

function Assert-FcNativeToolingLineUnion([int] $InputCount, [object] $Merged) {
    $rawRows = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $InputCount; $index++) {
        $name = 'input-' + $index.ToString('D4') + '.cobertura'
        $report = Read-FcNativeMergeCoverage (Join-Path $OutputDirectory $name) $MaximumFileBytes
        foreach ($row in $report.rawRows) { $rawRows.Add($row) }
    }
    if ($rawRows.Count -eq 0) { throw $script:FcNativeMergeCounts.Invalid }
    $expected = Merge-FcNativeCoverageLineRows $rawRows.ToArray()
    Assert-FcNativeMergeRowsEqual $expected $Merged.rows
}

function Export-FcNativeToolingInputs([string[]] $Paths) {
    for ($index = 0; $index -lt $Paths.Count; $index++) {
        $name = 'input-' + $index.ToString('D4') + '.cobertura'
        Invoke-FcNativeMergeLogged $ToolPackageRoot ([string[]] @($Paths[$index])) $OutputDirectory $name 'cobertura' `
            $TimeoutSeconds $MaximumOutputCharacters $MaximumFileBytes
    }
}

function Export-FcNativeToolingMerged([string] $Merged) {
    $input = [string[]] @($Merged)
    Invoke-FcNativeMergeLogged $ToolPackageRoot $input $OutputDirectory 'merged.xml' 'xml' `
        $TimeoutSeconds $MaximumOutputCharacters $MaximumFileBytes
    Invoke-FcNativeMergeLogged $ToolPackageRoot $input $OutputDirectory 'merged.cobertura' 'cobertura' `
        $TimeoutSeconds $MaximumOutputCharacters $MaximumFileBytes
}

function Export-FcNativeToolingControl([string] $Control) {
    $input = [string[]] @($Control)
    Invoke-FcNativeMergeLogged $ToolPackageRoot $input $OutputDirectory 'repeated-input-control.xml' 'xml' `
        $TimeoutSeconds $MaximumOutputCharacters $MaximumFileBytes
    Invoke-FcNativeMergeLogged $ToolPackageRoot $input $OutputDirectory 'repeated-input-control.cobertura' 'cobertura' `
        $TimeoutSeconds $MaximumOutputCharacters $MaximumFileBytes
}

function Assert-FcNativeToolingInputsUnchanged([object[]] $Inputs) {
    foreach ($item in $Inputs) {
        $after = Read-FcNativeBoundedFile $item.path $MaximumReportBytes $ReadBufferBytes 'The native coverage tooling proof input changed.'
        if ($after.length -ne $item.length -or $after.sha256 -cne $item.sha256) {
            throw 'The native coverage tooling proof input changed.'
        }
    }
    $descriptor = Read-FcNativeBoundedFile $script:FcNativeToolingDescriptor.path $MaximumDescriptorBytes $ReadBufferBytes 'The native coverage tooling descriptor changed.'
    if ($descriptor.length -ne $script:FcNativeToolingDescriptor.length -or
        $descriptor.sha256 -cne $script:FcNativeToolingDescriptor.sha256) { throw 'The native coverage tooling descriptor changed.' }
}

function Write-FcNativeToolingProof([object[]] $Inputs, [object] $Counts) {
    $proof = [ordered]@{ schemaVersion = 1; proofKind = 'native-tooling-only'; inputCount = $Inputs.Count
        packageCounts = $Counts.packages; lineCounts = [ordered]@{ covered = $Counts.linesCovered; valid = $Counts.linesValid }
        fileCounts = $Counts.files; uncoveredLocations = $Counts.uncoveredLocations
        nativeCoberturaBranchPairs = $Counts.nativeCoberturaBranchPairs
        branchUnion = $Counts.branchUnion
        repeatedInputInvariant = $true; productQualification = $false }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $proof -Depth 12 -Compress) + "`n")
    if ($bytes.LongLength -gt $MaximumFileBytes) { throw $script:FcNativeMergeCounts.Invalid }
    Write-FcNativeCreateOnly (Join-Path $OutputDirectory 'tooling-proof.json') $bytes
    [ordered]@{ schemaVersion = $proof.schemaVersion; proofKind = $proof.proofKind; inputCount = $proof.inputCount
        repeatedInputInvariant = $proof.repeatedInputInvariant; productQualification = $proof.productQualification
        proofFile = 'tooling-proof.json' }
}
