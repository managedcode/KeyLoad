function Write-FcCreateOnly([string] $Path, [byte[]] $Bytes) {
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


$script:FcMergeOperationIndex = 0
function Invoke-FcMergeLogged([string] $ToolRoot, [string[]] $Inputs, [string] $OutputDirectory,
    [string] $OutputName, [string] $Format, [int] $TimeoutSeconds, [int] $MaximumOutputCharacters, [long] $MaximumOutputBytes) {
    $output = Join-Path $OutputDirectory $OutputName
    $logName = 'execution-operation-' + $script:FcMergeOperationIndex.ToString('D4') + '.log'
    $log = Join-Path $OutputDirectory $logName
    $script:FcMergeOperationIndex++
    $primary = $null; $result = $null
    try { $result = Invoke-FcCoverageMerge $ToolRoot $Inputs $output $log $Format $TimeoutSeconds $MaximumOutputCharacters $MaximumOutputBytes }
    catch [System.Exception] { $primary = $_.Exception }
    $settlement = $null
    try { Assert-FcActiveInputsUnchanged }
    catch [System.Exception] { $settlement = $_.Exception }
    if ($null -ne $primary -and $null -ne $settlement) { throw [AggregateException]::new($primary,$settlement) }
    if ($null -ne $primary) { throw $primary }
    if ($null -ne $settlement) { throw $settlement }
    $result
}

function Assert-FcActiveInputsUnchanged {
    if ($null -ne $script:FcMergeInput.ActivePlan) { Assert-FcMergeInputsUnchanged $script:FcMergeInput.ActivePlan }
    if ($null -ne $script:FcMergeInput.ActiveToolingInputs) { Assert-FcToolingInputsUnchanged $script:FcMergeInput.ActiveToolingInputs }
}

function Invoke-FcMergePlan([object] $Plan) {
    $bounds = $Plan.descriptor.bounds
    $script:FcMergeInput.ReadBufferBytes = [int] $bounds.readBufferBytes
    $script:FcMergeInput.ActivePlan = $Plan
    $script:FcMergeInput.ActiveToolingInputs = $null
    $output = Resolve-FcEvidencePath $Plan.evidenceRoot $Plan.descriptor.outputDirectory $bounds.maximumPathCharacters
    if (Test-Path -LiteralPath $output) { throw 'The execution coverage merge output directory already exists.' }
    [void] [IO.Directory]::CreateDirectory($output)
    $inputs = @($Plan.inputs | ForEach-Object { $_.path })
    $counts = Invoke-FcMergeOutputs $Plan $output $inputs
    Assert-FcMergeInputsUnchanged $Plan
    Assert-FcCurrentSourceGeneration $Plan
    $inputRows = @($Plan.inputs | ForEach-Object { [ordered]@{ kind = $_.kind; path = $_.path; length = $_.length; sha256 = $_.sha256 } })
    $outputRows = Get-FcMergeOutputRows $output $bounds
    Assert-FcMergeInputsUnchanged $Plan
    Assert-FcCurrentSourceGeneration $Plan
    Write-FcMergeReceipt $Plan $output $inputRows $outputRows $counts
    throw $script:FcMergeInput.ProcessAttributionOpen
}

function Invoke-FcMergeOutputs([object] $Plan, [string] $Output, [string[]] $Inputs) {
    Export-FcIndividualReports $Plan $Output
    Assert-FcClusterServerCoverage $Plan $Output
    Invoke-FcMergeCommands $Plan $Output $Inputs
    Invoke-FcRepeatedInputCommands $Plan $Output $Inputs
    $bounds = $Plan.descriptor.bounds
    $individual = Read-FcIndividualCoverage $Plan $Output
    $merged = Read-FcMergeCoverage (Join-Path $Output 'merged.cobertura') $bounds.maximumReportBytes $Plan.manifest
    $control = Read-FcMergeCoverage (Join-Path $Output 'repeated-input-control.cobertura') $bounds.maximumReportBytes $Plan.manifest
    Assert-FcMergedLineUnion $individual $merged
    Assert-FcMergeCountsEqual $merged $control
    $merged
}

function Read-FcIndividualCoverage([object] $Plan, [string] $Output) {
    $reports = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $Plan.inputs.Count; $index++) {
        $name = 'input-' + $index.ToString('D4') + '.cobertura'
        $path = Join-Path $Output $name
        $reports.Add((Read-FcMergeCoverage $path $Plan.descriptor.bounds.maximumReportBytes $Plan.manifest))
    }
    if ($reports.Count -ne $Plan.inputs.Count -or $reports.Count -eq 0) { throw $script:FcMergeInput.InvalidEvidence }
    $reports.ToArray()
}

function Assert-FcMergedLineUnion([object[]] $Inputs, [object] $Merged) {
    $rawRows = [Collections.Generic.List[object]]::new()
    foreach ($report in $Inputs) { foreach ($row in $report.rawRows) { $rawRows.Add($row) } }
    $expected = Merge-FcCoverageLineRows $rawRows.ToArray()
    Assert-FcMergeRowsEqual $expected $Merged.rows
}

function Export-FcIndividualReports([object] $Plan, [string] $Output) {
    $bounds = $Plan.descriptor.bounds
    for ($index = 0; $index -lt $Plan.inputs.Count; $index++) {
        $name = 'input-' + $index.ToString('D4') + '.cobertura'
        $item = $Plan.inputs[$index]
        Invoke-FcMergeLogged $ToolPackageRoot ([string[]] @($item.path)) $Output $name 'cobertura' `
            ([int] $bounds.applicationCleanupTimeoutSeconds) ([int] $bounds.maximumManifestBytes) ([long] $bounds.maximumFileBytes)
    }
}

function Invoke-FcMergeCommands([object] $Plan, [string] $Output, [string[]] $Inputs) {
    $bounds = $Plan.descriptor.bounds
    $timeout = [int] $bounds.applicationCleanupTimeoutSeconds
    $characters = [int] $bounds.maximumManifestBytes
    $bytes = [long] $bounds.maximumFileBytes
    Invoke-FcMergeLogged $ToolPackageRoot $Inputs $Output 'merged.coverage' 'coverage' $timeout $characters $bytes
    $mergedInput = [string[]] @((Join-Path $Output 'merged.coverage'))
    Invoke-FcMergeLogged $ToolPackageRoot $mergedInput $Output 'merged.xml' 'xml' $timeout $characters $bytes
    Invoke-FcMergeLogged $ToolPackageRoot $mergedInput $Output 'merged.cobertura' 'cobertura' $timeout $characters $bytes
}

function Invoke-FcRepeatedInputCommands([object] $Plan, [string] $Output, [string[]] $Inputs) {
    $twice = [Collections.Generic.List[string]]::new()
    foreach ($path in $Inputs) { $twice.Add($path) }
    foreach ($path in $Inputs) { $twice.Add($path) }
    $bounds = $Plan.descriptor.bounds
    $timeout = [int] $bounds.applicationCleanupTimeoutSeconds
    $characters = [int] $bounds.maximumManifestBytes
    $bytes = [long] $bounds.maximumFileBytes
    Invoke-FcMergeLogged $ToolPackageRoot $twice.ToArray() $Output 'repeated-input-control.coverage' 'coverage' `
        $timeout $characters $bytes
    $controlInput = [string[]] @((Join-Path $Output 'repeated-input-control.coverage'))
    Invoke-FcMergeLogged $ToolPackageRoot $controlInput $Output 'repeated-input-control.xml' 'xml' $timeout $characters $bytes
    Invoke-FcMergeLogged $ToolPackageRoot $controlInput $Output 'repeated-input-control.cobertura' 'cobertura' $timeout $characters $bytes
}

function Assert-FcMergeInputsUnchanged([object] $Plan) {
    $bounds = $Plan.descriptor.bounds
    foreach ($item in $Plan.inputs) {
        $now = Read-FcHashFile $item.path $bounds.maximumReportBytes $bounds.readBufferBytes $script:FcMergeInput.InvalidEvidence
        if ($now.length -ne $item.length -or $now.sha256 -cne $item.sha256) { throw $script:FcMergeInput.InvalidEvidence }
    }
    foreach ($path in $script:FcMergeInput.ReferenceCatalog.Keys) {
        $reference = $script:FcMergeInput.ReferenceCatalog[$path]
        $after = Read-FcHashFile $path $bounds.maximumFileBytes $bounds.readBufferBytes $script:FcMergeInput.InvalidEvidence
        if ($after.length -ne $reference.length -or $after.sha256 -cne $reference.sha256) { throw $script:FcMergeInput.InvalidEvidence }
    }
}

function Get-FcMergeOutputRows([string] $Output, [object] $Bounds) {
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $Output -File -Force)) {
        if ($rows.Count -ge $Bounds.maximumFiles -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw $script:FcMergeInput.InvalidEvidence
        }
        $read = Read-FcBoundedFile $file.FullName $Bounds.maximumFileBytes $Bounds.readBufferBytes $script:FcMergeInput.InvalidEvidence
        $rows.Add([ordered]@{ path = $file.Name; length = $read.length; sha256 = $read.sha256 })
    }
    $rows.ToArray()
}

function Write-FcMergeReceipt([object] $Plan, [string] $Output, [object[]] $Inputs,
    [object[]] $Outputs, [object] $Counts) {
    $bounds = $Plan.descriptor.bounds
    $receipt = [ordered]@{
        schemaVersion = 1; invocationId = $Plan.descriptor.invocationId
        sourceManifestSha256 = $Plan.descriptor.sourceManifest.sha256; toolVersion = $ToolVersion
        inputCount = $Plan.inputs.Count; inputs = $Inputs; outputs = $Outputs; packageCounts = $Counts.packages
        fileCounts = $Counts.files; uncoveredLocations = $Counts.uncoveredLocations
        lineCounts = [ordered]@{ covered = $Counts.linesCovered; valid = $Counts.linesValid }
        coberturaBranchPairs = $Counts.coberturaBranchPairs
        branchUnion = $Counts.branchUnion
        controlInvariant = $true; productQualification = $script:FcMergeInput.ProcessAttributionOpen
        processLifecycleOriginals = $Plan.descriptor.clusterLifecycles
    }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $receipt -Depth 16 -Compress) + "`n")
    if ($bytes.Length -gt $bounds.maximumManifestBytes) { throw $script:FcMergeInput.InvalidEvidence }
    Write-FcCreateOnly (Join-Path $Output 'merge-receipt.json') $bytes
    [ordered]@{ outputDirectory = $Output; inputCount = $Plan.inputs.Count; lineCounts = $receipt.lineCounts
        coberturaBranchPairs = $receipt.coberturaBranchPairs; branchUnion = $receipt.branchUnion }
}

function Assert-FcClusterServerCoverage([object] $Plan, [string] $Output) {
    $cluster = @($Plan.inputs | Where-Object { $_.kind -ceq 'suite' -and $_.suite -ceq 'cluster' })
    if ($cluster.Count -ne 1) { throw $script:FcMergeInput.InvalidEvidence }
    $index = [Array]::IndexOf([object[]] $Plan.inputs, $cluster[0])
    $path = Join-Path $Output ('input-' + $index.ToString('D4') + '.cobertura')
    $report = Read-FcMergeCoverage $path $Plan.descriptor.bounds.maximumReportBytes $Plan.manifest
    if (-not $report.packages.ContainsKey('KeyLoad') -or
        $report.packages['KeyLoad'].linesValid -le 0 -or $report.packages['KeyLoad'].linesCovered -le 0) {
        throw 'The original execution cluster MTP report contains no executed source-bound KeyLoad code.'
    }
}
