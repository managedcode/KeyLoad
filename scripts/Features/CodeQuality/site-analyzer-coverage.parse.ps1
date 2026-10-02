function Read-CoberturaDocument([string] $CoverageReportPath) {
    $tokens = $script:CoverageTokens
    if (-not [IO.File]::Exists($CoverageReportPath)) { throw $tokens.ErrorMissingCobertura }
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = $tokens.MaxXmlCharacters
    $settings.IgnoreComments = $true
    $settings.IgnoreWhitespace = $true
    $reader = [Xml.XmlReader]::Create($CoverageReportPath, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    if ($document.DocumentElement.LocalName -cne $tokens.XmlRoot) { throw $tokens.ErrorMalformedCobertura }
    $document
}

function Find-CoverageModulePackage([Xml.XmlDocument] $Document) {
    $tokens = $script:CoverageTokens
    $packages = @($Document.DocumentElement.SelectNodes($tokens.XmlPackageSelector) |
        Where-Object { $_.GetAttribute($tokens.XmlPackageName) -ceq $tokens.ModuleName })
    if ($packages.Count -ne 1) { throw $tokens.ErrorNoModule }
    $packages[0]
}

function Resolve-CoberturaSourcePath(
    [string] $RepositoryRoot,
    [string] $Filename,
    [string[]] $SourceRoots) {
    $tokens = $script:CoverageTokens
    if ([string]::IsNullOrWhiteSpace($Filename) -or $Filename.Contains($tokens.PathColon)) { throw $tokens.ErrorUnsafePath }
    if ([IO.Path]::IsPathRooted($Filename)) {
        $candidate = [IO.Path]::GetFullPath($Filename)
    }
    else {
        $base = $RepositoryRoot
        if ($SourceRoots.Count -eq 1) { $base = $SourceRoots[0] }
        elseif ($SourceRoots.Count -gt 1) { throw $tokens.ErrorUnsafePath }
        $candidate = [IO.Path]::GetFullPath([IO.Path]::Combine($base, $Filename))
    }

    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $candidate)
    if ([IO.Path]::IsPathRooted($relative) -or
        $relative -eq $tokens.ParentPathSegment -or
        $relative.StartsWith($tokens.ParentPathSegment + [string] $tokens.PathSeparator, $tokens.RepositoryPathCompare)) {
        throw $tokens.ErrorUnsafePath
    }

    $relative.Replace([string] $tokens.AlternatePathSeparator, [string] $tokens.PathSeparator).Replace([string] $tokens.PathBackslash, $tokens.PathSlash)
}

function Get-CoberturaSourceRoots([Xml.XmlDocument] $Document, [string] $RepositoryRoot) {
    $tokens = $script:CoverageTokens
    $nodes = @($Document.DocumentElement.SelectNodes($tokens.XmlSourceSelector))
    if ($nodes.Count -gt 1) { throw $tokens.ErrorUnsafePath }
    if ($nodes.Count -eq 0) { return @() }
    $source = [string] $nodes[0].InnerText
    if ([string]::IsNullOrWhiteSpace($source)) { throw $tokens.ErrorUnsafePath }
    if ([IO.Path]::IsPathRooted($source)) { $fullPath = [IO.Path]::GetFullPath($source) }
    else { $fullPath = [IO.Path]::GetFullPath([IO.Path]::Combine($RepositoryRoot, $source)) }
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $fullPath)
    if ([IO.Path]::IsPathRooted($relative) -or
        $relative -eq $tokens.ParentPathSegment -or
        $relative.StartsWith($tokens.ParentPathSegment + [string] $tokens.PathSeparator, $tokens.RepositoryPathCompare)) {
        throw $tokens.ErrorUnsafePath
    }

    @($fullPath)
}

function Convert-CoberturaInteger([string] $Value) {
    $parsed = 0L
    if (-not [long]::TryParse($Value, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed) -or $parsed -lt 0) {
        throw $script:CoverageTokens.ErrorInvalidCounter
    }

    $parsed
}

function Add-CoverageInt64([long] $Current, [long] $Increment) {
    $tokens = $script:CoverageTokens
    if ($Current -lt 0 -or $Increment -lt 0) { throw $tokens.ErrorInvalidCounter }
    $sum = [System.Numerics.BigInteger]::Add([System.Numerics.BigInteger] $Current, [System.Numerics.BigInteger] $Increment)
    if ([System.Numerics.BigInteger]::Compare($sum, [System.Numerics.BigInteger] [long]::MaxValue) -gt 0) {
        throw $tokens.ErrorCounterOverflow
    }
    [long] $sum
}

function Read-CoberturaBranchPair([string] $Value) {
    $match = [regex]::Match($Value, $script:CoverageTokens.BranchCoveragePattern)
    if (-not $match.Success) { throw $script:CoverageTokens.ErrorInvalidBranch }
    $covered = Convert-CoberturaInteger $match.Groups[1].Value
    $total = Convert-CoberturaInteger $match.Groups[2].Value
    if ($total -le 0) { throw $script:CoverageTokens.ErrorEmptyDenominator }
    if ($covered -gt $total) { throw $script:CoverageTokens.ErrorPercentBounds }
    $pair = [ordered]@{}
    $pair[$script:CoverageTokens.ValueCovered] = $covered
    $pair[$script:CoverageTokens.ValueTotal] = $total
    $pair
}

function Add-CoberturaLine(
    [Collections.IDictionary] $LineRecords,
    [Collections.IDictionary] $LineUnion,
    [Collections.IDictionary] $BranchRecords,
    [string] $RelativePath,
    [string] $ClassName,
    [Xml.XmlElement] $LineElement,
    [long] $MaximumSourceLine) {
    $tokens = $script:CoverageTokens
    $lineNumber = Convert-CoberturaInteger $LineElement.GetAttribute($tokens.XmlLineNumber)
    $hits = Convert-CoberturaInteger $LineElement.GetAttribute($tokens.XmlHits)
    if ($lineNumber -le 0) { throw $tokens.ErrorInvalidCounter }
    if ($lineNumber -gt $MaximumSourceLine) { throw $tokens.ErrorLineBeyondSource }
    $branchFlag = $LineElement.GetAttribute($tokens.XmlBranch)
    if ($branchFlag.Length -gt 0) {
        if ([string]::Equals($branchFlag, $tokens.XmlBranchTrue, [StringComparison]::OrdinalIgnoreCase)) {
            $branchFlag = $tokens.XmlBranchTrue
        }
        elseif ([string]::Equals($branchFlag, $tokens.XmlBranchFalse, [StringComparison]::OrdinalIgnoreCase)) {
            $branchFlag = $tokens.XmlBranchFalse
        }
        else { throw $tokens.ErrorInvalidBranch }
    }
    $branchPair = $null
    if ($branchFlag -ceq $tokens.XmlBranchTrue) {
        $branchPair = Read-CoberturaBranchPair $LineElement.GetAttribute($tokens.XmlConditionCoverage)
    }
    $lineIdentity = ConvertTo-Json -InputObject @($RelativePath, $ClassName, $lineNumber) -Compress
    Assert-CoberturaLineDuplicate $LineRecords $lineIdentity $hits $branchFlag $branchPair
    $lineRecord = [ordered]@{}
    $lineRecord[$tokens.ValueHits] = $hits
    $lineRecord[$tokens.ValueBranch] = $branchFlag
    if ($null -ne $branchPair) { $lineRecord[$tokens.ValueCovered] = $branchPair[$tokens.ValueCovered]; $lineRecord[$tokens.ValueTotal] = $branchPair[$tokens.ValueTotal] }
    $LineRecords[$lineIdentity] = $lineRecord
    $unionIdentity = ConvertTo-Json -InputObject @($RelativePath, $lineNumber) -Compress
    if (-not $LineUnion.ContainsKey($unionIdentity)) {
        $entry = [ordered]@{}
        $entry[$tokens.JsonPath] = $RelativePath
        $entry[$tokens.ValueLine] = $lineNumber
        $entry[$tokens.ValueCovered] = $false
        $LineUnion[$unionIdentity] = $entry
    }
    if ($hits -gt 0) { $LineUnion[$unionIdentity][$tokens.ValueCovered] = $true }

    if ($null -ne $branchPair) {
        Add-CoberturaBranch $BranchRecords $RelativePath $ClassName $lineNumber $branchPair
    }
}

function Assert-CoberturaLineDuplicate(
    [Collections.IDictionary] $LineRecords,
    [string] $Identity,
    [long] $Hits,
    [string] $BranchFlag,
    [Collections.IDictionary] $BranchPair) {
    $tokens = $script:CoverageTokens
    if (-not $LineRecords.ContainsKey($Identity)) { return }
    $existing = $LineRecords[$Identity]
    if ($existing[$tokens.ValueHits] -ne $Hits -or $existing[$tokens.ValueBranch] -cne $BranchFlag) { throw $tokens.ErrorDuplicateConflict }
    if ($null -ne $BranchPair -and
        ($existing[$tokens.ValueCovered] -ne $BranchPair[$tokens.ValueCovered] -or $existing[$tokens.ValueTotal] -ne $BranchPair[$tokens.ValueTotal])) {
        throw $tokens.ErrorDuplicateConflict
    }
}

function Add-CoberturaBranch(
    [Collections.IDictionary] $BranchRecords,
    [string] $RelativePath,
    [string] $ClassName,
    [long] $LineNumber,
    [Collections.IDictionary] $Pair) {
    $tokens = $script:CoverageTokens
    $identity = ConvertTo-Json -InputObject @($script:CoverageTokens.ModuleName, $ClassName, $RelativePath, $LineNumber) -Compress
    if ($BranchRecords.ContainsKey($identity)) {
        $existing = $BranchRecords[$identity]
        if ($existing[$tokens.ValueCovered] -ne $Pair[$tokens.ValueCovered] -or $existing[$tokens.ValueTotal] -ne $Pair[$tokens.ValueTotal]) {
            throw $script:CoverageTokens.ErrorDuplicateConflict
        }
        return
    }

    $entry = [ordered]@{}
    $entry[$tokens.ValueIdentity] = $identity
    $entry[$tokens.JsonPath] = $RelativePath
    $entry[$tokens.ValueCovered] = $Pair[$tokens.ValueCovered]
    $entry[$tokens.ValueTotal] = $Pair[$tokens.ValueTotal]
    $BranchRecords[$identity] = $entry
}

function Read-CoberturaRecords([Xml.XmlDocument] $Document, [string] $RepositoryRoot, [object] $Contract) {
    $tokens = $script:CoverageTokens
    $package = Find-CoverageModulePackage $Document
    $sourceRoots = Get-CoberturaSourceRoots $Document $RepositoryRoot
    $sourceKinds = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Contract[$tokens.JsonSources]) {
        $sourceKinds.Add([string] $source[$tokens.JsonPath], [string] $source[$tokens.JsonClassification])
    }
    $sourceLineCounts = Get-CoberturaSourceLineCounts $RepositoryRoot $Contract

    $lineRecords = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $lineUnion = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $branchRecords = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $classElements = @($package.SelectNodes($tokens.XmlClassSelector))
    foreach ($class in $classElements) {
        $className = $class.GetAttribute($tokens.XmlClassName)
        if ([string]::IsNullOrWhiteSpace($className)) { throw $tokens.ErrorNoClassName }
        $relativePath = Resolve-CoberturaSourcePath $RepositoryRoot $class.GetAttribute($tokens.XmlFilename) $sourceRoots
        if (-not $sourceKinds.ContainsKey($relativePath) -or $sourceKinds[$relativePath] -cne $tokens.ExecutableKind) {
            throw $tokens.ErrorUnknownSource
        }

        foreach ($line in @($class.SelectNodes($tokens.XmlLineSelector))) {
            Add-CoberturaLine $lineRecords $lineUnion $branchRecords $relativePath $className $line $sourceLineCounts[$relativePath]
        }
    }

    $records = [ordered]@{}
    $records[$tokens.ValueSourceKinds] = $sourceKinds
    $records[$tokens.ValueLineUnion] = $lineUnion
    $records[$tokens.ValueBranchRecords] = $branchRecords
    $records
}

function Get-CoberturaSourceLineCounts([string] $RepositoryRoot, [object] $Contract) {
    $tokens = $script:CoverageTokens
    $counts = [Collections.Generic.Dictionary[string, long]]::new([StringComparer]::Ordinal)
    foreach ($source in $Contract[$tokens.JsonSources]) {
        if ($source[$tokens.JsonClassification] -cne $tokens.ExecutableKind) { continue }
        $relative = [string] $source[$tokens.JsonPath]
        $path = Resolve-CoverageRepositoryFile $RepositoryRoot $relative
        $lines = [IO.File]::ReadAllLines($path)
        $counts.Add($relative, [long] $lines.Length)
    }

    $counts
}

function Get-FileCoverageRows([object] $Records, [object] $Contract) {
    $tokens = $script:CoverageTokens
    $rows = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($source in $Contract[$tokens.JsonSources]) {
        if ($source[$tokens.JsonClassification] -ceq $tokens.ExecutableKind) {
            $row = [ordered]@{}
            $row[$tokens.JsonPath] = [string] $source[$tokens.JsonPath]
            $row[$tokens.JsonLinesCovered] = 0L
            $row[$tokens.JsonLinesValid] = 0L
            $row[$tokens.JsonBranchesCovered] = 0L
            $row[$tokens.JsonBranchesValid] = 0L
            $rows.Add([string] $source[$tokens.JsonPath], $row)
        }
    }

    foreach ($line in $Records[$tokens.ValueLineUnion].Values) {
        $row = $rows[[string] $line[$tokens.JsonPath]]
        $row[$tokens.JsonLinesValid] = Add-CoverageInt64 $row[$tokens.JsonLinesValid] 1L
        if ($line[$tokens.ValueCovered]) { $row[$tokens.JsonLinesCovered] = Add-CoverageInt64 $row[$tokens.JsonLinesCovered] 1L }
    }
    foreach ($branch in $Records[$tokens.ValueBranchRecords].Values) {
        $row = $rows[[string] $branch[$tokens.JsonPath]]
        $row[$tokens.JsonBranchesCovered] = Add-CoverageInt64 $row[$tokens.JsonBranchesCovered] $branch[$tokens.ValueCovered]
        $row[$tokens.JsonBranchesValid] = Add-CoverageInt64 $row[$tokens.JsonBranchesValid] $branch[$tokens.ValueTotal]
    }

    foreach ($path in $rows.Keys) {
        if ($rows[$path][$tokens.JsonLinesValid] -le 0) { throw $tokens.ErrorMissingLine }
    }
    @($rows.Values | Sort-Object -Property path)
}

function Get-PipelineCoverageRows([object[]] $Files, [object] $Contract) {
    $tokens = $script:CoverageTokens
    $results = [Collections.Generic.List[object]]::new()
    foreach ($pipeline in $Contract[$tokens.JsonPipelines]) {
        $selected = @($Files | Where-Object { $pipeline[$tokens.JsonSources] -ccontains $_[$tokens.JsonPath] })
        $covered = [long] 0
        $total = [long] 0
        foreach ($file in $selected) {
            $covered = Add-CoverageInt64 $covered $file[$tokens.JsonLinesCovered]
            $total = Add-CoverageInt64 $total $file[$tokens.JsonLinesValid]
        }

        $ratio = Get-CoverageRatios $covered $total ([int] $Contract[$tokens.JsonThresholds][$tokens.JsonCriticalLinePercent])
        $result = [ordered]@{}
        $result[$tokens.JsonDiagnostic] = [string] $pipeline[$tokens.JsonDiagnostic]
        $result[$tokens.JsonLinesCovered] = $ratio[$tokens.ValueCovered]
        $result[$tokens.JsonLinesValid] = $ratio[$tokens.ValueTotal]
        $result[$tokens.ValuePassed] = $ratio[$tokens.ValuePassed]
        $results.Add($result)
    }

    @($results)
}

function New-CoverageReport([object[]] $Files, [object[]] $Pipelines, [object] $Contract, [string] $Revision, [string] $ReportHash) {
    $tokens = $script:CoverageTokens
    $lineCovered = [long] 0
    $lineTotal = [long] 0
    $branchCovered = [long] 0
    $branchTotal = [long] 0
    foreach ($file in $Files) {
        $lineCovered = Add-CoverageInt64 $lineCovered $file[$tokens.JsonLinesCovered]
        $lineTotal = Add-CoverageInt64 $lineTotal $file[$tokens.JsonLinesValid]
        $branchCovered = Add-CoverageInt64 $branchCovered $file[$tokens.JsonBranchesCovered]
        $branchTotal = Add-CoverageInt64 $branchTotal $file[$tokens.JsonBranchesValid]
    }

    $lineRatio = Get-CoverageRatios $lineCovered $lineTotal ([int] $Contract[$tokens.JsonThresholds][$tokens.JsonModuleLinePercent])
    $branchRatio = Get-CoverageRatios $branchCovered $branchTotal ([int] $Contract[$tokens.JsonThresholds][$tokens.JsonModuleBranchPercent])
    $failures = [Collections.Generic.List[string]]::new()
    if (-not $lineRatio[$tokens.ValuePassed]) { $failures.Add($tokens.FailureModuleLines) }
    if (-not $branchRatio[$tokens.ValuePassed]) { $failures.Add($tokens.FailureModuleBranches) }
    foreach ($pipeline in $Pipelines) {
        if (-not $pipeline[$tokens.ValuePassed]) { $failures.Add([string] $pipeline[$tokens.JsonDiagnostic]) }
    }

    $module = [ordered]@{}
    $module[$tokens.JsonLinesCovered] = $lineCovered
    $module[$tokens.JsonLinesValid] = $lineTotal
    $module[$tokens.JsonBranchesCovered] = $branchCovered
    $module[$tokens.JsonBranchesValid] = $branchTotal
    $report = [ordered]@{}
    $report[$tokens.JsonSchemaVersion] = $tokens.SchemaVersion
    $report[$tokens.JsonSourceRevision] = $Revision
    $report[$tokens.JsonNativeReportHash] = $ReportHash
    $report[$tokens.JsonModule] = $module
    $report[$tokens.JsonFiles] = $Files
    $report[$tokens.JsonCriticalPipelines] = $Pipelines
    $report[$tokens.JsonThresholdsUsed] = $Contract[$tokens.JsonThresholds]
    $report[$tokens.JsonPassed] = ($failures.Count -eq 0)
    $report[$tokens.JsonFailures] = @($failures)
    $report
}

function Get-CoverageRatios([long] $Covered, [long] $Total, [int] $Threshold) {
    if ($Total -le 0) { throw $script:CoverageTokens.ErrorEmptyDenominator }
    $ratio = [ordered]@{}
    $ratio[$script:CoverageTokens.ValueCovered] = $Covered
    $ratio[$script:CoverageTokens.ValueTotal] = $Total
    $coveredProduct = [System.Numerics.BigInteger]::Multiply([System.Numerics.BigInteger] $Covered, [System.Numerics.BigInteger] 100)
    $thresholdProduct = [System.Numerics.BigInteger]::Multiply([System.Numerics.BigInteger] $Threshold, [System.Numerics.BigInteger] $Total)
    $ratio[$script:CoverageTokens.ValuePassed] = ([System.Numerics.BigInteger]::Compare($coveredProduct, $thresholdProduct) -ge 0)
    $ratio
}

function New-CoberturaFailureReport([object] $Revision, [object] $ReportHash, [object] $Contract, [string] $Reason) {
    $tokens = $script:CoverageTokens
    $report = [ordered]@{}
    $report[$tokens.JsonSchemaVersion] = $tokens.SchemaVersion
    $report[$tokens.JsonSourceRevision] = $Revision
    $report[$tokens.JsonNativeReportHash] = $ReportHash
    $report[$tokens.JsonModule] = $null
    $report[$tokens.JsonFiles] = @()
    $report[$tokens.JsonCriticalPipelines] = @()
    $report[$tokens.JsonThresholdsUsed] = if ($null -ne $Contract) { $Contract[$tokens.JsonThresholds] } else { $null }
    $report[$tokens.JsonPassed] = $false
    $report[$tokens.JsonFailures] = @($Reason)
    $report
}
