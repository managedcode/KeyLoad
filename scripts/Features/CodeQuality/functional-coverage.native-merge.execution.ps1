function Write-FcNativeCreateOnly([string] $Path, [byte[]] $Bytes) {
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $failure = $null
    try { $stream.Write($Bytes); $stream.Flush($true) }
    catch [System.Exception] { $failure = $_.Exception }
    finally {
        try { $stream.Dispose() }
        catch [System.Exception] { if ($null -eq $failure) { $failure = $_.Exception } else { $failure = [AggregateException]::new($failure, $_.Exception) } }
    }
    if ($null -ne $failure) { throw $failure }
}


$script:FcNativeMergeOperationIndex = 0
function Invoke-FcNativeMergeLogged([string] $ToolRoot, [string[]] $Inputs, [string] $OutputDirectory,
    [string] $OutputName, [string] $Format, [int] $TimeoutSeconds, [int] $MaximumOutputCharacters, [long] $MaximumOutputBytes) {
    $output = Join-Path $OutputDirectory $OutputName
    $logName = 'native-operation-' + $script:FcNativeMergeOperationIndex.ToString('D4') + '.log'
    $log = Join-Path $OutputDirectory $logName
    $script:FcNativeMergeOperationIndex++
    $primary = $null; $result = $null
    try { $result = Invoke-FcCoverageMerge $ToolRoot $Inputs $output $log $Format $TimeoutSeconds $MaximumOutputCharacters $MaximumOutputBytes }
    catch [System.Exception] { $primary = $_.Exception }
    $settlement = $null
    try { Assert-FcNativeActiveInputsUnchanged }
    catch [System.Exception] { $settlement = $_.Exception }
    if ($null -ne $primary -and $null -ne $settlement) { throw [AggregateException]::new($primary,$settlement) }
    if ($null -ne $primary) { throw $primary }
    if ($null -ne $settlement) { throw $settlement }
    $result
}

function Assert-FcNativeActiveInputsUnchanged {
    if ($null -ne $script:FcNativeMergeInput.ActivePlan) { Assert-FcNativeMergeInputsUnchanged $script:FcNativeMergeInput.ActivePlan }
    if ($null -ne $script:FcNativeMergeInput.ActiveToolingInputs) { Assert-FcNativeToolingInputsUnchanged $script:FcNativeMergeInput.ActiveToolingInputs }
}

function Invoke-FcNativeMergePlan([object] $Plan) {
    $bounds = $Plan.descriptor.bounds
    $script:FcNativeMergeInput.ReadBufferBytes = [int] $bounds.readBufferBytes
    $script:FcNativeMergeInput.ActivePlan = $Plan
    $script:FcNativeMergeInput.ActiveToolingInputs = $null
    $output = Resolve-FcNativeEvidencePath $Plan.evidenceRoot $Plan.descriptor.outputDirectory $bounds.maximumPathCharacters
    if (Test-Path -LiteralPath $output) { throw 'The native coverage merge output directory already exists.' }
    [void] [IO.Directory]::CreateDirectory($output)
    $inputs = @($Plan.inputs | ForEach-Object { $_.path })
    $counts = Invoke-FcNativeMergeOutputs $Plan $output $inputs
    Assert-FcNativeMergeInputsUnchanged $Plan
    Assert-FcNativeCurrentSourceGeneration $Plan
    $inputRows = @($Plan.inputs | ForEach-Object { [ordered]@{ kind = $_.kind; path = $_.path; length = $_.length; sha256 = $_.sha256 } })
    $outputRows = Get-FcNativeMergeOutputRows $output $bounds
    Assert-FcNativeMergeInputsUnchanged $Plan
    Assert-FcNativeCurrentSourceGeneration $Plan
    Write-FcNativeMergeReceipt $Plan $output $inputRows $outputRows $counts
}

function Invoke-FcNativeMergeOutputs([object] $Plan, [string] $Output, [string[]] $Inputs) {
    Export-FcNativeIndividualReports $Plan $Output
    Invoke-FcNativeMergeCommands $Plan $Output $Inputs
    Invoke-FcNativeRepeatedInputCommands $Plan $Output $Inputs
    $bounds = $Plan.descriptor.bounds
    $individual = Read-FcNativeIndividualCoverage $Plan $Output
    $merged = Read-FcNativeMergeCoverage (Join-Path $Output 'merged.cobertura') $bounds.maximumReportBytes $Plan.manifest
    $control = Read-FcNativeMergeCoverage (Join-Path $Output 'repeated-input-control.cobertura') $bounds.maximumReportBytes $Plan.manifest
    Assert-FcNativeMergedLineUnion $individual $merged
    Assert-FcNativeMergeCountsEqual $merged $control
    $merged
}

function Read-FcNativeIndividualCoverage([object] $Plan, [string] $Output) {
    $reports = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $Plan.inputs.Count; $index++) {
        $name = 'input-' + $index.ToString('D4') + '.cobertura'
        $path = Join-Path $Output $name
        $reports.Add((Read-FcNativeMergeCoverage $path $Plan.descriptor.bounds.maximumReportBytes $Plan.manifest))
    }
    if ($reports.Count -ne $Plan.inputs.Count -or $reports.Count -eq 0) { throw $script:FcNativeMergeInput.InvalidEvidence }
    $reports.ToArray()
}

function Assert-FcNativeMergedLineUnion([object[]] $Inputs, [object] $Merged) {
    $rawRows = [Collections.Generic.List[object]]::new()
    foreach ($report in $Inputs) { foreach ($row in $report.rawRows) { $rawRows.Add($row) } }
    $expected = Merge-FcNativeCoverageLineRows $rawRows.ToArray()
    Assert-FcNativeMergeRowsEqual $expected $Merged.rows
}

function Export-FcNativeIndividualReports([object] $Plan, [string] $Output) {
    $bounds = $Plan.descriptor.bounds
    for ($index = 0; $index -lt $Plan.inputs.Count; $index++) {
        $name = 'input-' + $index.ToString('D4') + '.cobertura'
        $item = $Plan.inputs[$index]
        Invoke-FcNativeMergeLogged $ToolPackageRoot ([string[]] @($item.path)) $Output $name 'cobertura' `
            ([int] $bounds.applicationCleanupTimeoutSeconds) ([int] $bounds.maximumManifestBytes) ([long] $bounds.maximumFileBytes)
    }
}

function Invoke-FcNativeMergeCommands([object] $Plan, [string] $Output, [string[]] $Inputs) {
    $bounds = $Plan.descriptor.bounds
    $timeout = [int] $bounds.applicationCleanupTimeoutSeconds
    $characters = [int] $bounds.maximumManifestBytes
    $bytes = [long] $bounds.maximumFileBytes
    Invoke-FcNativeMergeLogged $ToolPackageRoot $Inputs $Output 'merged.coverage' 'coverage' $timeout $characters $bytes
    $mergedInput = [string[]] @((Join-Path $Output 'merged.coverage'))
    Invoke-FcNativeMergeLogged $ToolPackageRoot $mergedInput $Output 'merged.xml' 'xml' $timeout $characters $bytes
    Invoke-FcNativeMergeLogged $ToolPackageRoot $mergedInput $Output 'merged.cobertura' 'cobertura' $timeout $characters $bytes
}

function Invoke-FcNativeRepeatedInputCommands([object] $Plan, [string] $Output, [string[]] $Inputs) {
    $twice = [Collections.Generic.List[string]]::new()
    foreach ($path in $Inputs) { $twice.Add($path) }
    foreach ($path in $Inputs) { $twice.Add($path) }
    $bounds = $Plan.descriptor.bounds
    $timeout = [int] $bounds.applicationCleanupTimeoutSeconds
    $characters = [int] $bounds.maximumManifestBytes
    $bytes = [long] $bounds.maximumFileBytes
    Invoke-FcNativeMergeLogged $ToolPackageRoot $twice.ToArray() $Output 'repeated-input-control.coverage' 'coverage' `
        $timeout $characters $bytes
    $controlInput = [string[]] @((Join-Path $Output 'repeated-input-control.coverage'))
    Invoke-FcNativeMergeLogged $ToolPackageRoot $controlInput $Output 'repeated-input-control.xml' 'xml' $timeout $characters $bytes
    Invoke-FcNativeMergeLogged $ToolPackageRoot $controlInput $Output 'repeated-input-control.cobertura' 'cobertura' $timeout $characters $bytes
}

function Assert-FcNativeMergeInputsUnchanged([object] $Plan) {
    $bounds = $Plan.descriptor.bounds
    foreach ($item in $Plan.inputs) {
        $now = Read-FcNativeHashFile $item.path $bounds.maximumReportBytes $bounds.readBufferBytes $script:FcNativeMergeInput.InvalidEvidence
        if ($now.length -ne $item.length -or $now.sha256 -cne $item.sha256) { throw $script:FcNativeMergeInput.InvalidEvidence }
    }
    foreach ($path in $script:FcNativeMergeInput.ReferenceCatalog.Keys) {
        $reference = $script:FcNativeMergeInput.ReferenceCatalog[$path]
        $after = Read-FcNativeHashFile $path $bounds.maximumFileBytes $bounds.readBufferBytes $script:FcNativeMergeInput.InvalidEvidence
        if ($after.length -ne $reference.length -or $after.sha256 -cne $reference.sha256) { throw $script:FcNativeMergeInput.InvalidEvidence }
    }
}

function Get-FcNativeMergeOutputRows([string] $Output, [object] $Bounds) {
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $Output -File -Force)) {
        if ($rows.Count -ge $Bounds.maximumFiles -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw $script:FcNativeMergeInput.InvalidEvidence
        }
        $read = Read-FcNativeBoundedFile $file.FullName $Bounds.maximumFileBytes $Bounds.readBufferBytes $script:FcNativeMergeInput.InvalidEvidence
        $rows.Add([ordered]@{ path = $file.Name; length = $read.length; sha256 = $read.sha256 })
    }
    $rows.ToArray()
}

function Write-FcNativeMergeReceipt([object] $Plan, [string] $Output, [object[]] $Inputs,
    [object[]] $Outputs, [object] $Counts) {
    $bounds = $Plan.descriptor.bounds
    $receipt = [ordered]@{
        schemaVersion = 1; invocationId = $Plan.descriptor.invocationId
        sourceManifestSha256 = $Plan.descriptor.sourceManifest.sha256; toolVersion = $ToolVersion
        inputCount = $Plan.inputs.Count; inputs = $Inputs; outputs = $Outputs; packageCounts = $Counts.packages
        fileCounts = $Counts.files; uncoveredLocations = $Counts.uncoveredLocations
        lineCounts = [ordered]@{ covered = $Counts.linesCovered; valid = $Counts.linesValid }
        nativeCoberturaBranchPairs = $Counts.nativeCoberturaBranchPairs
        branchUnion = $Counts.branchUnion
        controlInvariant = $true; productQualification = 'not established by merge tooling source'
    }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $receipt -Depth 16 -Compress) + "`n")
    if ($bytes.Length -gt $bounds.maximumManifestBytes) { throw $script:FcNativeMergeInput.InvalidEvidence }
    Write-FcNativeCreateOnly (Join-Path $Output 'merge-receipt.json') $bytes
    [ordered]@{ outputDirectory = $Output; inputCount = $Plan.inputs.Count; lineCounts = $receipt.lineCounts
        nativeCoberturaBranchPairs = $receipt.nativeCoberturaBranchPairs; branchUnion = $receipt.branchUnion }
}
