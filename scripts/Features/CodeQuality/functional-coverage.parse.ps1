function Read-FcXml([string] $Path, [string] $ExpectedRoot) {
    if (-not [IO.File]::Exists($Path)) { throw $script:FunctionalCoverage.ErrorIncomplete }
    if ((Get-Item -LiteralPath $Path).Length -gt $script:FunctionalCoverage.MaxXmlBytes) { throw $script:FunctionalCoverage.ErrorCoverage }
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = $script:FunctionalCoverage.MaxXmlCharacters
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $doc = [Xml.XmlDocument]::new()
        $doc.XmlResolver = $null
        $doc.Load($reader)
    }
    finally { $reader.Dispose() }
    if ($null -eq $doc.DocumentElement -or $doc.DocumentElement.LocalName -cne $ExpectedRoot) {
        throw $script:FunctionalCoverage.ErrorCoverage
    }
    $doc
}

function Convert-FcCount([string] $Value) {
    $number = 0L
    if (-not [long]::TryParse($Value, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref] $number) -or $number -lt 0) {
        throw $script:FunctionalCoverage.ErrorInvalidCount
    }
    $number
}

function Resolve-FcReportSource([string] $Root, [string] $Filename, [string[]] $SourceRoots, [Collections.Generic.HashSet[string]] $Allowed) {
    if ([string]::IsNullOrWhiteSpace($Filename) -or $Filename.Contains(':')) { throw $script:FunctionalCoverage.ErrorUnexpectedSource }
    if ([IO.Path]::IsPathRooted($Filename)) { $candidate = [IO.Path]::GetFullPath($Filename) }
    else {
        if ($SourceRoots.Count -gt 1) { throw $script:FunctionalCoverage.ErrorUnexpectedSource }
        $base = $Root
        if ($SourceRoots.Count -eq 1) { $base = $SourceRoots[0] }
        $candidate = [IO.Path]::GetFullPath([IO.Path]::Combine($base, $Filename))
    }
    $relative = [IO.Path]::GetRelativePath($Root, $candidate)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw $script:FunctionalCoverage.ErrorUnexpectedSource
    }
    $normalized = $relative.Replace([IO.Path]::DirectorySeparatorChar, '/')
    if (-not $Allowed.Contains($normalized)) { throw $script:FunctionalCoverage.ErrorUnexpectedSource }
    $normalized
}

function Read-FcCobertura([string] $Root, [string] $Path, [object[]] $Sources) {
    $t = $script:FunctionalCoverage
    $document = Read-FcXml $Path 'coverage'
    $packages = @($document.DocumentElement.SelectNodes('./packages/package'))
    if ($packages.Count -ne 1 -or $packages[0].GetAttribute('name') -cne $t.Module) { throw $t.ErrorUnexpectedModule }
    $sourceNodes = @($document.DocumentElement.SelectNodes('./sources/source'))
    if ($sourceNodes.Count -gt 1) { throw $t.ErrorUnexpectedSource }
    $sourceRoots = @()
    if ($sourceNodes.Count -eq 1) {
        $value = [string] $sourceNodes[0].InnerText
        if ([string]::IsNullOrWhiteSpace($value)) { throw $t.ErrorUnexpectedSource }
        if ([IO.Path]::IsPathRooted($value)) { $sourceRoots += [IO.Path]::GetFullPath($value) }
        else { $sourceRoots += [IO.Path]::GetFullPath([IO.Path]::Combine($Root, $value)) }
    }

    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $maximumLines = [Collections.Generic.Dictionary[string, long]]::new([StringComparer]::Ordinal)
    foreach ($source in $Sources) {
        $sourcePath = [string] $source.path
        [void] $allowed.Add($sourcePath)
        $maximumLines.Add($sourcePath, [long] @(Get-Content -LiteralPath (Join-Path $Root $sourcePath)).Count)
    }
    $lineRecords = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $lineUnion = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $branchRecords = [Collections.Generic.List[object]]::new()
    $seenSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($class in @($packages[0].SelectNodes('./classes/class'))) {
        $className = $class.GetAttribute('name')
        if ([string]::IsNullOrWhiteSpace($className)) { throw $t.ErrorCoverage }
        $source = Resolve-FcReportSource $Root $class.GetAttribute('filename') $sourceRoots $allowed
        [void] $seenSources.Add($source)
        foreach ($line in @($class.SelectNodes('./lines/line'))) {
            $number = Convert-FcCount $line.GetAttribute('number')
            $hits = Convert-FcCount $line.GetAttribute('hits')
            if ($number -le 0 -or $number -gt $maximumLines[$source]) { throw $t.ErrorInvalidCount }
            $identity = ConvertTo-Json -InputObject @($className, $source, $number) -Compress
            $branch = $null
            $branchFlag = $line.GetAttribute('branch').ToLowerInvariant()
            if ($branchFlag.Length -gt 0 -and $branchFlag -cnotin @('true','false')) { throw $t.ErrorInvalidCount }
            if ($branchFlag -ceq 'true') {
                $coverage = $line.GetAttribute('condition-coverage')
                $match = [regex]::Match($coverage, '\A(?:100(?:\.0{1,2})?|(?:0|[1-9][0-9]?)(?:\.[0-9]{1,2})?)% \(([0-9]+)/([0-9]+)\)\z')
                if (-not $match.Success) { throw $t.ErrorInvalidCount }
                $covered = Convert-FcCount $match.Groups[1].Value
                $total = Convert-FcCount $match.Groups[2].Value
                if ($total -le 0 -or $covered -gt $total) { throw $t.ErrorInvalidCount }
                $branch = [ordered]@{ source = $source; class = $className; line = $number; covered = $covered; total = $total }
            }
            $isNew = -not $lineRecords.ContainsKey($identity)
            if (-not $isNew) {
                $prior = $lineRecords[$identity]
                if ($prior.hits -ne $hits -or (($null -eq $prior.branch) -ne ($null -eq $branch)) -or
                    ($null -ne $branch -and ($prior.branch.covered -ne $branch.covered -or $prior.branch.total -ne $branch.total))) {
                    throw $t.ErrorInvalidCount
                }
            }
            else { $lineRecords.Add($identity, [ordered]@{ source = $source; line = $number; hits = $hits; branch = $branch }) }
            $unionId = ConvertTo-Json -InputObject @($source, $number) -Compress
            if (-not $lineUnion.ContainsKey($unionId)) { $lineUnion.Add($unionId, [ordered]@{ source = $source; line = $number; hit = $false }) }
            if ($hits -gt 0) { $lineUnion[$unionId].hit = $true }
            if ($null -ne $branch -and $isNew) { $branchRecords.Add($branch) }
        }
    }
    if ($lineRecords.Count -eq 0) { throw $t.ErrorIncomplete }
    $missing = @($Sources | Where-Object { -not $seenSources.Contains([string] $_.path) } | ForEach-Object { [string] $_.path })
    [ordered]@{ lines = @($lineRecords.Values); lineUnion = @($lineUnion.Values); branches = @($branchRecords); sourceCount = $seenSources.Count; sourcesWithoutLines = $missing }
}

function Read-FcTrx([string] $Path, [string] $Suite, [string[]] $ExpectedClasses) {
    $t = $script:FunctionalCoverage
    $doc = Read-FcXml $Path 'TestRun'
    $results = @($doc.SelectNodes('//*[local-name()="UnitTestResult"]'))
    $matched = @($results | Where-Object { $_.GetAttribute('testName').Contains('PartitionQuery') })
    if ($matched.Count -eq 0) { throw $t.ErrorTrx }
    $notPassed = @($matched | Where-Object { $_.GetAttribute('outcome') -cne 'Passed' })
    if ($notPassed.Count -gt 0) { throw $t.ErrorTrx }
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($test in $matched) {
        $name = $test.GetAttribute('testName')
        $matches = @($ExpectedClasses | Where-Object { $name.Contains($_) })
        if ($matches.Count -ne 1) { throw $t.ErrorTrx }
        [void] $observed.Add([string] $matches[0])
    }
    if (-not $observed.SetEquals($ExpectedClasses)) { throw $t.ErrorTrx }
    [ordered]@{ suite = $Suite; testCount = $matched.Count; tests = @($matched | ForEach-Object { $_.GetAttribute('testName') } | Sort-Object -Unique) }
}

function New-FcReport([object[]] $Runs, [string] $Filter) {
    $firstTests = @($Runs[0].trx.tests | Sort-Object -Unique) -join "`n"
    $secondTests = @($Runs[1].trx.tests | Sort-Object -Unique) -join "`n"
    if ($firstTests -cne $secondTests) { throw $script:FunctionalCoverage.ErrorTrx }
    $union = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $unreportedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($run in $Runs) {
        foreach ($source in $run.coverage.sourcesWithoutLines) { [void] $unreportedSources.Add([string] $source) }
        foreach ($line in $run.coverage.lineUnion) {
            $identity = ConvertTo-Json -InputObject @($line.source, $line.line) -Compress
            if (-not $union.ContainsKey($identity)) { $union.Add($identity, [ordered]@{ source = $line.source; line = $line.line; hit = [bool] $line.hit }) }
            elseif ($line.hit) { $union[$identity].hit = $true }
        }
    }
    $locations = @($union.Values | Where-Object { -not $_.hit } | Sort-Object source,line | ForEach-Object { [ordered]@{ path = $_.source; line = $_.line } })
    $covered = @($union.Values | Where-Object hit).Count
    $total = $union.Count
    [ordered]@{
        schemaVersion = 1
        module = 'KeyLoad.Query'
        requirements = @('REQ-CQ-009','AC-CQ-018','AC-CQ-019')
        scope = "Filter $Filter through Aspire unit and unit-scalar; KeyLoad.Query production module only"
        filter = $Filter
        lineHitUnion = $script:FunctionalCoverage.HitUnion
        mergedBranches = 'unmeasured; no branch-outcome identity union is available'
        unionLineCounts = [ordered]@{ covered = $covered; total = $total }
        uncoveredLocations = $locations
        sourceFilesWithoutLineRecords = @($unreportedSources | Sort-Object)
        runs = @($Runs | ForEach-Object { [ordered]@{
            suite = $_.trx.suite
            processExitCode = 0
            partitionQueryTests = $_.trx.testCount
            testNames = $_.trx.tests
            nativeReportSha256 = $_.reportHash
            lines = [ordered]@{ covered = @($_.coverage.lineUnion | Where-Object hit).Count; total = $_.coverage.lineUnion.Count }
            sourceFilesWithoutLines = $_.coverage.sourcesWithoutLines
            nativeBranches = $_.coverage.branches
        } })
        thresholds = $null
        qualification = 'private scoped profile only; raw integer coverage data, no threshold pass; does not close AC-CQ-009 or product/RF3 coverage'
    }
}

function Convert-FcReportToMarkdown([object] $Report) {
    $builder = [Text.StringBuilder]::new()
    [void] $builder.AppendLine('# KeyLoad.Query functional coverage')
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('Private `PartitionQuery*` profile through Aspire `unit` and `unit-scalar` (`unit-scalar` disables hardware intrinsics). This report records raw counts and uncovered locations; it applies no threshold and does not qualify complete product or RF3 coverage.')
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('| Run | PartitionQuery tests | Lines covered | Lines reported | Native branch pairs |')
    [void] $builder.AppendLine('|---|---:|---:|---:|---:|')
    foreach ($run in $Report.runs) {
        [void] $builder.AppendLine("| $($run.suite) | $($run.partitionQueryTests) | $($run.lines.covered) | $($run.lines.total) | $($run.nativeBranches.Count) native pairs retained |")
    }
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine("Merged line-hit union: $($Report.unionLineCounts.covered)/$($Report.unionLineCounts.total). Merged branches: unmeasured.")
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('Source files without line records are listed separately; they have no line location to report.')
    foreach ($source in $Report.sourceFilesWithoutLineRecords) { [void] $builder.AppendLine("- No line records: ``$source``") }
    if ($Report.sourceFilesWithoutLineRecords.Count -eq 0) { [void] $builder.AppendLine('- Every inventoried source contributed line records.') }
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('## Uncovered locations')
    foreach ($location in $Report.uncoveredLocations) { [void] $builder.AppendLine("- ``$($location.path):$($location.line)``") }
    if ($Report.uncoveredLocations.Count -eq 0) { [void] $builder.AppendLine('- None in the reported line inventory.') }
    $builder.ToString()
}
