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

function Add-FcCount([long] $Current, [long] $Increment) {
    $sum = [System.Numerics.BigInteger]::Add([System.Numerics.BigInteger] $Current, [System.Numerics.BigInteger] $Increment)
    if ($Current -lt 0 -or $Increment -lt 0 -or
        [System.Numerics.BigInteger]::Compare($sum, [System.Numerics.BigInteger] [long]::MaxValue) -gt 0) {
        throw $script:FunctionalCoverage.ErrorInvalidCount
    }
    [long] $sum
}

function Get-FcPercent([long] $Covered, [long] $Total) {
    if ($Total -eq 0) { return $null }
    [decimal]::Round(([decimal] $Covered * 100) / [decimal] $Total, 2, [MidpointRounding]::AwayFromZero)
}

function Get-FcFileCoverage([object[]] $Lines, [object[]] $Branches, [object[]] $Sources, [bool] $IncludeBranches = $true) {
    $rows = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($source in $Sources) {
        $path = [string] $source.path
        $rows.Add($path, [ordered]@{ path = $path; linesCovered = 0L; linesValid = 0L; branchesCovered = 0L; branchesValid = 0L })
    }
    foreach ($line in $Lines) {
        if (-not $rows.ContainsKey([string] $line.source)) { throw $script:FunctionalCoverage.ErrorUnexpectedSource }
        $row = $rows[[string] $line.source]
        $row.linesValid = Add-FcCount $row.linesValid 1L
        $hit = if ($line.Contains('hits')) { [long] $line.hits -gt 0 } else { [bool] $line.hit }
        if ($hit) { $row.linesCovered = Add-FcCount $row.linesCovered 1L }
    }
    foreach ($branch in $Branches) {
        if (-not $rows.ContainsKey([string] $branch.source)) { throw $script:FunctionalCoverage.ErrorUnexpectedSource }
        $row = $rows[[string] $branch.source]
        $row.branchesCovered = Add-FcCount $row.branchesCovered ([long] $branch.covered)
        $row.branchesValid = Add-FcCount $row.branchesValid ([long] $branch.total)
    }
    @($rows.Values | Sort-Object path | ForEach-Object {
        $branchSummary = $null
        if ($IncludeBranches) {
            $branchSummary = [ordered]@{ covered = $_.branchesCovered; valid = $_.branchesValid; percent = Get-FcPercent $_.branchesCovered $_.branchesValid }
        }
        [ordered]@{
            path = $_.path
            lines = [ordered]@{ covered = $_.linesCovered; valid = $_.linesValid; percent = Get-FcPercent $_.linesCovered $_.linesValid }
            nativeBranches = $branchSummary
        }
    })
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
    if ($sourceNodes.Count -gt 1 -or $Sources.Count -gt $t.MaxSources) { throw $t.ErrorUnexpectedSource }
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
    $sourcesWithLineRecords = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($class in @($packages[0].SelectNodes('./classes/class'))) {
        $className = $class.GetAttribute('name')
        if ([string]::IsNullOrWhiteSpace($className)) { throw $t.ErrorCoverage }
        $source = Resolve-FcReportSource $Root $class.GetAttribute('filename') $sourceRoots $allowed
        [void] $seenSources.Add($source)
        if ($seenSources.Count -gt $t.MaxSources) { throw $t.ErrorUnexpectedSource }
        foreach ($line in @($class.SelectNodes('./lines/line'))) {
            [void] $sourcesWithLineRecords.Add($source)
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
            if (-not $lineUnion.ContainsKey($unionId)) {
                if ($lineUnion.Count -ge $t.MaxDistinctLineLocations) { throw $t.ErrorCoverage }
                $lineUnion.Add($unionId, [ordered]@{ source = $source; line = $number; hit = $false })
            }
            if ($hits -gt 0) { $lineUnion[$unionId].hit = $true }
            if ($null -ne $branch -and $isNew) { $branchRecords.Add($branch) }
        }
    }
    if ($lineRecords.Count -eq 0) { throw $t.ErrorIncomplete }
    $nativeLinesValid = Convert-FcCount $document.DocumentElement.GetAttribute('lines-valid')
    $nativeLinesCovered = Convert-FcCount $document.DocumentElement.GetAttribute('lines-covered')
    $nativeBranchesValid = Convert-FcCount $document.DocumentElement.GetAttribute('branches-valid')
    $nativeBranchesCovered = Convert-FcCount $document.DocumentElement.GetAttribute('branches-covered')
    $lineRecordCovered = @($lineRecords.Values | Where-Object { $_.hits -gt 0 }).Count
    $branchRecordCovered = 0L; $branchRecordTotal = 0L
    foreach ($branch in $branchRecords) {
        $branchRecordCovered = Add-FcCount $branchRecordCovered ([long] $branch.covered)
        $branchRecordTotal = Add-FcCount $branchRecordTotal ([long] $branch.total)
    }
    if ($nativeLinesValid -ne $lineRecords.Count -or $nativeLinesCovered -ne $lineRecordCovered -or
        $nativeBranchesValid -ne $branchRecordTotal -or $nativeBranchesCovered -ne $branchRecordCovered) { throw $t.ErrorInvalidCount }
    $missing = @($Sources | Where-Object { -not $sourcesWithLineRecords.Contains([string] $_.path) } | ForEach-Object { [string] $_.path })
    [ordered]@{
        lines = @($lineRecords.Values)
        lineUnion = @($lineUnion.Values)
        branches = @($branchRecords)
        sourceCount = $seenSources.Count
        sourcesWithoutLines = $missing
        nativeLines = [ordered]@{ covered = $nativeLinesCovered; valid = $nativeLinesValid; percent = Get-FcPercent $nativeLinesCovered $nativeLinesValid }
        nativeBranches = [ordered]@{ covered = $nativeBranchesCovered; valid = $nativeBranchesValid; percent = Get-FcPercent $nativeBranchesCovered $nativeBranchesValid }
    }
}

function Read-FcTrx([string] $Path, [string] $Suite, [object[]] $ExpectedCases) {
    $t = $script:FunctionalCoverage
    $doc = Read-FcXml $Path 'TestRun'
    $definitions = @($doc.SelectNodes('//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]'))
    $results = @($doc.SelectNodes('//*[local-name()="Results"]/*[local-name()="UnitTestResult"]'))
    if ($definitions.Count -ne $ExpectedCases.Count -or $results.Count -ne $ExpectedCases.Count) { throw $t.ErrorTrx }

    $expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($case in $ExpectedCases) { $expected.Add("$($case.className)|$($case.methodName)", $case) }
    $selectedDefinitions = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $id = $definition.GetAttribute('id')
        $method = $definition.SelectSingleNode('./*[local-name()="TestMethod"]')
        if ([string]::IsNullOrWhiteSpace($id) -or $null -eq $method -or $selectedDefinitions.ContainsKey($id)) { throw $t.ErrorTrx }
        $className = $method.GetAttribute('className')
        $methodName = $method.GetAttribute('name')
        $identity = "$className|$methodName"
        if (-not $expected.ContainsKey($identity) -or $definition.GetAttribute('name') -cne $methodName -or -not $observed.Add($identity)) { throw $t.ErrorTrx }
        $selectedDefinitions.Add($id, [ordered]@{ className = $className; methodName = $methodName; expected = $expected[$identity] })
    }
    if ($observed.Count -ne $expected.Count) { throw $t.ErrorTrx }

    $resultsById = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $cases = [Collections.Generic.List[object]]::new()
    foreach ($result in $results) {
        $id = $result.GetAttribute('testId')
        if (-not $selectedDefinitions.ContainsKey($id) -or $resultsById.ContainsKey($id)) { throw $t.ErrorTrx }
        if ($result.GetAttribute('outcome') -cne 'Passed') { throw $t.ErrorTrx }
        $resultsById.Add($id, $result)
        $definition = $selectedDefinitions[$id]
        $testName = $result.GetAttribute('testName')
        if ($testName -cne $definition.expected.instanceName) { throw $t.ErrorTrx }
        $caseIdentity = "$id|$($definition.className)|$($definition.methodName)|$testName"
        $cases.Add([ordered]@{ identity = $caseIdentity; testId = $id; className = $definition.className; methodName = $definition.methodName; testName = $testName })
    }
    if ($resultsById.Count -ne $selectedDefinitions.Count) { throw $t.ErrorTrx }

    $counters = $doc.SelectSingleNode('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
    if ($null -eq $counters -or (Convert-FcCount $counters.GetAttribute('total')) -ne $definitions.Count -or
        (Convert-FcCount $counters.GetAttribute('executed')) -ne $results.Count -or
        (Convert-FcCount $counters.GetAttribute('passed')) -ne $results.Count) { throw $t.ErrorTrx }
    foreach ($counterName in @('failed','error','timeout','aborted','inconclusive','notRunnable','notExecuted','disconnected','passedButRunAborted','inProgress','pending')) {
        $value = $counters.GetAttribute($counterName)
        if ($value.Length -gt 0 -and (Convert-FcCount $value) -ne 0) { throw $t.ErrorTrx }
    }
    [ordered]@{ suite = $Suite; testCount = $cases.Count; definitionsCount = $definitions.Count; resultsCount = $results.Count; cases = @($cases | Sort-Object identity) }
}

function New-FcReport([object[]] $Runs, [string] $Filter, [object[]] $Sources, [object] $Manifest, [string] $ManifestPath) {
    $firstTests = @($Runs[0].trx.cases | ForEach-Object { $_.identity } | Sort-Object -Unique) -join "`n"
    $secondTests = @($Runs[1].trx.cases | ForEach-Object { $_.identity } | Sort-Object -Unique) -join "`n"
    if ($firstTests -cne $secondTests) { throw $script:FunctionalCoverage.ErrorTrx }
    $union = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $unreportedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($run in $Runs) {
        foreach ($source in $run.coverage.sourcesWithoutLines) { [void] $unreportedSources.Add([string] $source) }
        foreach ($line in $run.coverage.lineUnion) {
            $identity = ConvertTo-Json -InputObject @($line.source, $line.line) -Compress
            if (-not $union.ContainsKey($identity)) {
                if ($union.Count -ge $script:FunctionalCoverage.MaxDistinctLineLocations) { throw $script:FunctionalCoverage.ErrorCoverage }
                $union.Add($identity, [ordered]@{ source = $line.source; line = $line.line; hit = [bool] $line.hit })
            }
            elseif ($line.hit) { $union[$identity].hit = $true }
        }
    }
    $locations = @($union.Values | Where-Object { -not $_.hit } | Sort-Object source,line | ForEach-Object { [ordered]@{ path = $_.source; line = $_.line } })
    $covered = @($union.Values | Where-Object hit).Count
    $total = $union.Count
    $unionFiles = @(Get-FcFileCoverage @($union.Values) @() $Sources $false)
    $runReports = @($Runs | ForEach-Object {
        $fileCoverage = @(Get-FcFileCoverage $_.coverage.lines $_.coverage.branches $Sources)
        $distinctCovered = @($_.coverage.lineUnion | Where-Object hit).Count
        $distinctValid = $_.coverage.lineUnion.Count
        [ordered]@{
            suite = $_.trx.suite
            processExitCode = 0
            testDefinitions = $_.trx.definitionsCount
            testResults = $_.trx.resultsCount
            testCases = $_.trx.cases
            nativeReportSha256 = $_.reportHash
            trxSha256 = $_.trxHash
            lines = $_.coverage.nativeLines
            distinctSourceLineLocations = [ordered]@{
                covered = $distinctCovered
                valid = $distinctValid
                percent = Get-FcPercent $distinctCovered $distinctValid
            }
            files = $fileCoverage
            sourceFilesWithoutLines = $_.coverage.sourcesWithoutLines
            nativeBranchPairs = $_.coverage.branches
        }
    })
    $manifestInfo = [ordered]@{ path = [IO.Path]::GetFileName($ManifestPath); sha256 = Get-FcHash $ManifestPath }
    [ordered]@{
        schemaVersion = $script:FunctionalCoverage.SchemaVersion
        module = 'KeyLoad.Query'
        requirements = @('REQ-CQ-009','AC-CQ-018','AC-CQ-019','AC-CQ-039')
        scope = "Filter $Filter through Aspire unit and unit-scalar; KeyLoad.Query production module only"
        filter = $Filter
        lineHitUnion = $script:FunctionalCoverage.HitUnion
        mergedBranches = 'unmeasured; no branch-outcome identity union is available'
        sourceManifest = $manifestInfo
        sourceIdentities = @($Manifest.sources)
        contributorIdentities = @($Manifest.contributors)
        moduleArtifacts = @($Manifest.deployment)
        unionLineCounts = [ordered]@{ covered = $covered; valid = $total; percent = Get-FcPercent $covered $total }
        files = $unionFiles
        uncoveredLocations = $locations
        sourceFilesWithoutLineRecords = @($unreportedSources | Sort-Object)
        runs = $runReports
        thresholds = $null
        qualification = 'private exact Query case profile only; every admitted class, method, and instance appears once per TRX with a passing result; no threshold pass; does not close AC-CQ-009 or product/RF3 coverage'
    }
}

function Convert-FcReportToMarkdown([object] $Report) {
    $builder = [Text.StringBuilder]::new()
    [void] $builder.AppendLine('# KeyLoad.Query functional coverage')
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('Private exact `PartitionQuery` case profile through Aspire `unit` and `unit-scalar` (`unit-scalar` disables hardware intrinsics). This report records raw counts and uncovered locations; it applies no threshold and does not qualify complete product or RF3 coverage.')
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('| Run | TRX cases | Native lines covered | Native lines valid | Native line percent | Native branch pairs |')
    [void] $builder.AppendLine('|---|---:|---:|---:|---:|---:|')
    foreach ($run in $Report.runs) {
        [void] $builder.AppendLine("| $($run.suite) | $($run.testResults) | $($run.lines.covered) | $($run.lines.valid) | $($run.lines.percent)% | $($run.nativeBranchPairs.Count) pairs |")
    }
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine("Merged distinct source-line hit union: $($Report.unionLineCounts.covered)/$($Report.unionLineCounts.valid) ($($Report.unionLineCounts.percent)%). Merged branches: unmeasured.")
    [void] $builder.AppendLine('')
    [void] $builder.AppendLine('## Merged per-source line counts')
    [void] $builder.AppendLine('| Source | Covered | Valid | Percent |')
    [void] $builder.AppendLine('|---|---:|---:|---:|')
    foreach ($file in $Report.files) {
        $percent = if ($null -eq $file.lines.percent) { 'unmeasured' } else { "$($file.lines.percent)%" }
        [void] $builder.AppendLine("| ``$($file.path)`` | $($file.lines.covered) | $($file.lines.valid) | $percent |")
    }
    foreach ($run in $Report.runs) {
        [void] $builder.AppendLine('')
        [void] $builder.AppendLine("## $($run.suite) native per-source counts")
        [void] $builder.AppendLine('| Source | Native lines covered | Native lines valid | Native line percent | Native branches covered | Native branches valid | Native branch percent |')
        [void] $builder.AppendLine('|---|---:|---:|---:|---:|---:|---:|')
        foreach ($file in $run.files) {
            $linePercent = if ($null -eq $file.lines.percent) { 'unmeasured' } else { "$($file.lines.percent)%" }
            $branchCovered = if ($null -eq $file.nativeBranches) { 'unmeasured' } else { [string] $file.nativeBranches.covered }
            $branchValid = if ($null -eq $file.nativeBranches) { 'unmeasured' } else { [string] $file.nativeBranches.valid }
            $branchPercent = if ($null -eq $file.nativeBranches -or $null -eq $file.nativeBranches.percent) { 'unmeasured' } else { "$($file.nativeBranches.percent)%" }
            [void] $builder.AppendLine("| ``$($file.path)`` | $($file.lines.covered) | $($file.lines.valid) | $linePercent | $branchCovered | $branchValid | $branchPercent |")
        }
    }
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
