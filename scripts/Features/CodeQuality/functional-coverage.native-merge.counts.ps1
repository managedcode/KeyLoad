$script:FcNativeMergeCounts = [ordered]@{
    Invalid = 'The native merged coverage report has invalid integer counts or identities.'
    RootName = 'coverage'
}

function Convert-FcNativeMergeCount([string] $Value) {
    $parsed = 0L
    if (-not [long]::TryParse($Value, [Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture, [ref] $parsed) -or $parsed -lt 0) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    $parsed
}

function Read-FcNativeMergeCoverage([string] $Path, [long] $MaximumBytes, [object] $Manifest = $null) {
    $root = Read-FcNativeCoverageRoot $Path $MaximumBytes
    $branchPairs = Read-FcNativeRootBranchPairs $root
    $totals = [ordered]@{
        linesValid = Convert-FcNativeMergeCount $root.GetAttribute('lines-valid')
        linesCovered = Convert-FcNativeMergeCount $root.GetAttribute('lines-covered')
        nativeCoberturaBranchPairs = $branchPairs
    }
    if ($totals.linesCovered -gt $totals.linesValid) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    $sourceMap = if ($null -eq $Manifest) { $null } else { Get-FcNativeProductionSourceMap $Manifest }
    $rawRows = Read-FcNativeCoverageRows $root $sourceMap
    Get-FcNativeCoverageSummary $rawRows $totals
}
function Read-FcNativeRootBranchPairs([Xml.XmlElement] $Root) {
    $hasValid = $Root.HasAttribute('branches-valid')
    $hasCovered = $Root.HasAttribute('branches-covered')
    if ($hasValid -ne $hasCovered) { throw $script:FcNativeMergeCounts.Invalid }
    if (-not $hasValid) { return $null }
    $valid = Convert-FcNativeMergeCount $Root.GetAttribute('branches-valid')
    $covered = Convert-FcNativeMergeCount $Root.GetAttribute('branches-covered')
    if ($covered -gt $valid) { throw $script:FcNativeMergeCounts.Invalid }
    [ordered]@{ valid = $valid; covered = $covered }
}
function Read-FcNativeCoverageRoot([string] $Path, [long] $MaximumBytes) {
    $info = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($info -isnot [IO.FileInfo] -or $info.Length -le 0 -or $info.Length -gt $MaximumBytes -or
        ($info.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = $MaximumBytes
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    if ($null -eq $document.DocumentElement -or $document.DocumentElement.LocalName -cne $script:FcNativeMergeCounts.RootName) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    $document.DocumentElement
}

function Read-FcNativeCoverageRows([Xml.XmlElement] $Root, [object] $SourceMap) {
    $rows = [Collections.Generic.List[object]]::new()
    $rowIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($package in @($Root.SelectNodes('./packages/package'))) {
        Add-FcNativeCoveragePackageRows $package $rows $rowIds $SourceMap
    }
    if ($rows.Count -eq 0) { throw $script:FcNativeMergeCounts.Invalid }
    $rows.ToArray()
}
function Add-FcNativeCoveragePackageRows([Xml.XmlElement] $Package, [Collections.Generic.List[object]] $Rows,
    [Collections.Generic.HashSet[string]] $RowIds, [object] $SourceMap) {
    $packageName = $Package.GetAttribute('name')
    if ([string]::IsNullOrWhiteSpace($packageName)) { throw $script:FcNativeMergeCounts.Invalid }
    if ($null -ne $SourceMap -and -not (Test-FcNativeCoveragePackage $packageName $SourceMap)) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    foreach ($class in @($Package.SelectNodes('./classes/class'))) {
        Add-FcNativeCoverageClassRows $packageName $class $Rows $RowIds $SourceMap
    }
}

function Test-FcNativeCoveragePackage([string] $Package, [Collections.Generic.Dictionary[string, object]] $SourceMap) {
    foreach ($mapping in $SourceMap.Values) { if ($mapping.package -ceq $Package) { return $true } }
    $false
}
function Add-FcNativeCoverageClassRows([string] $PackageName, [Xml.XmlElement] $Class,
    [Collections.Generic.List[object]] $Rows, [Collections.Generic.HashSet[string]] $RowIds, [object] $SourceMap) {
    $className = $Class.GetAttribute('name'); $fileName = $Class.GetAttribute('filename')
    if ([string]::IsNullOrWhiteSpace($className) -or [string]::IsNullOrWhiteSpace($fileName)) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    if ($null -ne $SourceMap) { $fileName = Resolve-FcNativeCoverageSource $PackageName $fileName $SourceMap }
    foreach ($line in @($Class.SelectNodes('./lines/line'))) {
        $row = Read-FcNativeCoverageLine $PackageName $fileName $className $line
        if (-not $RowIds.Add([string] $row.identity)) { throw $script:FcNativeMergeCounts.Invalid }
        $Rows.Add($row.value)
    }
}
function Read-FcNativeCoverageLine([string] $PackageName, [string] $FileName, [string] $ClassName,
    [Xml.XmlElement] $Line) {
    $number = Convert-FcNativeMergeCount $Line.GetAttribute('number')
    $hits = Convert-FcNativeMergeCount $Line.GetAttribute('hits')
    if ($number -le 0) { throw $script:FcNativeMergeCounts.Invalid }
    $branch = $null
    $branchFlag = $Line.GetAttribute('branch')
    if ($branchFlag -ieq 'true') { $branch = Read-FcNativeCoverageBranch $Line }
    elseif ($branchFlag.Length -gt 0 -and $branchFlag -ine 'false') { throw $script:FcNativeMergeCounts.Invalid }
    $identity = ConvertTo-Json -InputObject @($PackageName,$FileName,$ClassName,$number) -Compress
    $value = [ordered]@{ package = $PackageName; file = $FileName; class = $ClassName; line = $number; hits = $hits; branch = $branch }
    [ordered]@{ identity = $identity; value = $value }
}

function Read-FcNativeCoverageBranch([Xml.XmlElement] $Line) {
    $pattern = '\A(?:100(?:\.0{1,2})?|(?:0|[1-9][0-9]?)(?:\.[0-9]{1,2})?)% \(([0-9]+)/([0-9]+)\)\z'
    $match = [regex]::Match($Line.GetAttribute('condition-coverage'), $pattern)
    if (-not $match.Success) { throw $script:FcNativeMergeCounts.Invalid }
    $covered = Convert-FcNativeMergeCount $match.Groups[1].Value
    $total = Convert-FcNativeMergeCount $match.Groups[2].Value
    if ($total -le 0 -or $covered -gt $total) { throw $script:FcNativeMergeCounts.Invalid }
    [ordered]@{ covered = $covered; total = $total }
}

function Get-FcNativeProductionSourceMap([object] $Manifest) {
    $sourceMap = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($product in $Manifest.compiledProducts) {
        if ($product.role -cne 'production') { continue }
        $sources = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
        foreach ($source in $product.sources) { $sources.Add([string] $source.path, [string] $source.sha256) }
        $identity = $product.compiledIdentity
        if ($identity.compiledSourceBindingComplete -ne $true -or
            [string]::IsNullOrWhiteSpace($identity.originalCompilationRoot)) { throw $script:FcNativeMergeCounts.Invalid }
        foreach ($document in $identity.documents) {
            if ($document.generated -or -not $sources.ContainsKey([string] $document.path)) { continue }
            if ($document.sha256 -cne $sources[[string] $document.path]) { throw $script:FcNativeMergeCounts.Invalid }
            Add-FcNativeProductionSourceMapEntry $sourceMap ([string] $product.module) ([string] $document.path) `
                ([string] $identity.originalCompilationRoot)
        }
        foreach ($path in $sources.Keys) {
            $match = @($identity.documents | Where-Object { -not $_.generated -and $_.path -ceq $path })
            if ($match.Count -ne 1) { throw $script:FcNativeMergeCounts.Invalid }
        }
    }
    if ($sourceMap.Count -eq 0) { throw $script:FcNativeMergeCounts.Invalid }
    $sourceMap
}

function Add-FcNativeProductionSourceMapEntry([Collections.Generic.Dictionary[string, object]] $Map,
    [string] $Module, [string] $RelativeSource, [string] $CompilationRoot) {
    $root = [IO.Path]::GetFullPath($CompilationRoot)
    $relative = $RelativeSource.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $relative))
    $underRoot = [IO.Path]::GetRelativePath($root, $fullPath)
    if ([IO.Path]::IsPathRooted($underRoot) -or $underRoot -eq '..' -or
        $underRoot.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    $entry = [ordered]@{ package = $Module; source = $RelativeSource }
    if ($Map.ContainsKey($RelativeSource) -or $Map.ContainsKey($fullPath)) { throw $script:FcNativeMergeCounts.Invalid }
    $Map.Add($RelativeSource, $entry)
    $Map.Add($fullPath, $entry)
}

function Resolve-FcNativeCoverageSource([string] $Package, [string] $FileName,
    [Collections.Generic.Dictionary[string, object]] $SourceMap) {
    $candidate = if ([IO.Path]::IsPathFullyQualified($FileName)) { [IO.Path]::GetFullPath($FileName) } else { $FileName }
    if (-not $SourceMap.ContainsKey($candidate)) { throw $script:FcNativeMergeCounts.Invalid }
    $mapping = $SourceMap[$candidate]
    if ($mapping.package -cne $Package) { throw $script:FcNativeMergeCounts.Invalid }
    [string] $mapping.source
}

function Get-FcNativeCoverageSummary([object[]] $RawRows, [Collections.IDictionary] $Totals) {
    $rawLinesCovered = 0L; $nativeBranchesValid = 0L; $nativeBranchesCovered = 0L
    $rawPackages = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $rawFiles = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($row in $RawRows) {
        if ($row.hits -gt 0) { $rawLinesCovered++ }
        if ($null -ne $row.branch) {
            $nativeBranchesValid += [long] $row.branch.total
            $nativeBranchesCovered += [long] $row.branch.covered
        }
        Add-FcNativeCoverageRawBranchSummary $rawPackages $row
        Add-FcNativeCoverageRawFileBranchSummary $rawFiles $row
    }
    if ($Totals.linesValid -ne $RawRows.Count -or $Totals.linesCovered -ne $rawLinesCovered) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    if ($null -ne $Totals.nativeCoberturaBranchPairs -and
        ($Totals.nativeCoberturaBranchPairs.valid -ne $nativeBranchesValid -or
            $Totals.nativeCoberturaBranchPairs.covered -ne $nativeBranchesCovered)) {
        throw $script:FcNativeMergeCounts.Invalid
    }
    $rows = Merge-FcNativeCoverageLineRows $RawRows
    $byPackage = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $byFile = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $uncovered = [Collections.Generic.List[object]]::new()
    $lineCovered = 0L
    foreach ($row in $rows) {
        if ($row.hits -gt 0) { $lineCovered++ }
        Add-FcNativeCoveragePackageLineSummary $byPackage $row
        Add-FcNativeCoverageFileLineSummary $byFile $row
        if ($row.hits -eq 0) { $uncovered.Add([ordered]@{ module = $row.package; source = $row.file; line = $row.line }) }
    }
    foreach ($packageName in $byPackage.Keys) {
        $branches = if ($rawPackages.ContainsKey($packageName)) { $rawPackages[$packageName] } else { $null }
        if ($null -ne $branches) {
            $byPackage[$packageName].nativeCoberturaBranchPairs = [ordered]@{
                valid = $branches.branchesValid; covered = $branches.branchesCovered }
        }
    }
    foreach ($file in $byFile.Values) {
        $key = ConvertTo-Json -InputObject @($file.module,$file.source) -Compress
        $branches = if ($rawFiles.ContainsKey($key)) { $rawFiles[$key] } else { $null }
        if ($null -ne $branches) {
            $file.nativeCoberturaBranchPairs = [ordered]@{ valid = $branches.branchesValid; covered = $branches.branchesCovered }
        }
    }
    [ordered]@{ linesValid = [long] $rows.Count; linesCovered = $lineCovered
        nativeCoberturaLinesValid = $Totals.linesValid; nativeCoberturaLinesCovered = $Totals.linesCovered
        nativeCoberturaBranchPairs = $Totals.nativeCoberturaBranchPairs
        branchUnion = 'unmeasured; Cobertura branch pairs do not identify individual native branch outcomes'
        packages = $byPackage; files = @($byFile.Values | Sort-Object { $_['module'] },{ $_['source'] })
        uncoveredLocations = @($uncovered.ToArray()); rows = $rows
        rawRows = @($RawRows | Sort-Object { $_['package'] },{ $_['file'] },{ $_['class'] },{ $_['line'] }) }
}
function Merge-FcNativeCoverageLineRows([object[]] $RawRows) {
    $lines = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($row in $RawRows) {
        $identity = ConvertTo-Json -InputObject @($row.package,$row.file,$row.line) -Compress
        if (-not $lines.ContainsKey($identity)) {
            $lines.Add($identity, [ordered]@{ package = $row.package; file = $row.file; line = $row.line; hits = $row.hits })
        }
        elseif ($row.hits -gt 0) { $lines[$identity].hits = $row.hits }
    }
    @($lines.Values | Sort-Object { $_['package'] },{ $_['file'] },{ $_['line'] })
}

function Add-FcNativeCoveragePackageLineSummary([Collections.Generic.Dictionary[string, object]] $Packages,
    [object] $Row) {
    if (-not $Packages.ContainsKey([string] $Row.package)) {
        $Packages.Add([string] $Row.package, [ordered]@{ linesValid = 0L; linesCovered = 0L
            nativeCoberturaBranchPairs = $null })
    }
    $package = $Packages[[string] $Row.package]
    $package.linesValid++
    if ($Row.hits -gt 0) { $package.linesCovered++ }
}

function Add-FcNativeCoverageFileLineSummary([Collections.Generic.Dictionary[string, object]] $Files, [object] $Row) {
    $identity = ConvertTo-Json -InputObject @($Row.package,$Row.file) -Compress
    if (-not $Files.ContainsKey($identity)) {
        $Files.Add($identity, [ordered]@{ module = $Row.package; source = $Row.file; linesValid = 0L
            linesCovered = 0L; nativeCoberturaBranchPairs = $null })
    }
    $file = $Files[$identity]
    $file.linesValid++
    if ($Row.hits -gt 0) { $file.linesCovered++ }
}

function Add-FcNativeCoverageRawBranchSummary([Collections.Generic.Dictionary[string, object]] $Packages,
    [object] $Row) {
    if (-not $Packages.ContainsKey([string] $Row.package)) {
        $Packages.Add([string] $Row.package, $null)
    }
    if ($null -ne $Row.branch) {
        if ($null -eq $Packages[[string] $Row.package]) {
            $Packages[[string] $Row.package] = [ordered]@{ branchesValid = 0L; branchesCovered = 0L }
        }
        $Packages[[string] $Row.package].branchesValid += [long] $Row.branch.total
        $Packages[[string] $Row.package].branchesCovered += [long] $Row.branch.covered
    }
}

function Add-FcNativeCoverageRawFileBranchSummary([Collections.Generic.Dictionary[string, object]] $Files, [object] $Row) {
    if ($null -eq $Row.branch) { return }
    $identity = ConvertTo-Json -InputObject @($Row.package,$Row.file) -Compress
    if (-not $Files.ContainsKey($identity)) { $Files.Add($identity, [ordered]@{ branchesValid = 0L; branchesCovered = 0L }) }
    $Files[$identity].branchesValid += [long] $Row.branch.total
    $Files[$identity].branchesCovered += [long] $Row.branch.covered
}
function Assert-FcNativeMergeCountsEqual([object] $Expected, [object] $Actual) {
    Assert-FcNativeMergeTotalsEqual $Expected $Actual
    Assert-FcNativeMergeRowsEqual $Expected.rows $Actual.rows
    Assert-FcNativeMergeRawRowsEqual $Expected.rawRows $Actual.rawRows
    Assert-FcNativeMergePackagesEqual $Expected.packages $Actual.packages
}
function Assert-FcNativeMergeTotalsEqual([object] $Expected, [object] $Actual) {
    foreach ($field in @('linesValid','linesCovered')) {
        if ($Expected[$field] -ne $Actual[$field]) { throw $script:FcNativeMergeCounts.Invalid }
    }
    Assert-FcNativeBranchPairsEqual $Expected.nativeCoberturaBranchPairs $Actual.nativeCoberturaBranchPairs
    if ($Expected.rows.Count -ne $Actual.rows.Count) { throw $script:FcNativeMergeCounts.Invalid }
}

function Assert-FcNativeMergeRowsEqual([object[]] $Expected, [object[]] $Actual) {
    if ($Expected.Count -ne $Actual.Count) { throw $script:FcNativeMergeCounts.Invalid }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        $left = $Expected[$index]; $right = $Actual[$index]
        if ($left.package -cne $right.package -or $left.file -cne $right.file -or
            $left.line -ne $right.line -or (($left.hits -gt 0) -ne ($right.hits -gt 0))) {
            throw $script:FcNativeMergeCounts.Invalid
        }
    }
}

function Assert-FcNativeMergeRawRowsEqual([object[]] $Expected, [object[]] $Actual) {
    if ($Expected.Count -ne $Actual.Count) { throw $script:FcNativeMergeCounts.Invalid }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        $left = $Expected[$index]; $right = $Actual[$index]
        if ($left.package -cne $right.package -or $left.file -cne $right.file -or $left.class -cne $right.class -or
            $left.line -ne $right.line -or (($left.hits -gt 0) -ne ($right.hits -gt 0)) -or
            (($null -eq $left.branch) -ne ($null -eq $right.branch))) { throw $script:FcNativeMergeCounts.Invalid }
        if ($null -ne $left.branch -and ($left.branch.covered -ne $right.branch.covered -or
            $left.branch.total -ne $right.branch.total)) { throw $script:FcNativeMergeCounts.Invalid }
    }
}
function Assert-FcNativeMergePackagesEqual([Collections.Generic.Dictionary[string, object]] $Expected,
    [Collections.Generic.Dictionary[string, object]] $Actual) {
    $leftNames = @($Expected.Keys | Sort-Object) -join "`n"
    $rightNames = @($Actual.Keys | Sort-Object) -join "`n"
    if ($leftNames -cne $rightNames) { throw $script:FcNativeMergeCounts.Invalid }
    foreach ($name in $Expected.Keys) {
        foreach ($field in @('linesValid','linesCovered')) {
            if ($Expected[$name][$field] -ne $Actual[$name][$field]) { throw $script:FcNativeMergeCounts.Invalid }
        }
        Assert-FcNativeBranchPairsEqual $Expected[$name].nativeCoberturaBranchPairs $Actual[$name].nativeCoberturaBranchPairs
    }
}
function Assert-FcNativeBranchPairsEqual([object] $Expected, [object] $Actual) {
    if (($null -eq $Expected) -ne ($null -eq $Actual)) { throw $script:FcNativeMergeCounts.Invalid }
    if ($null -ne $Expected -and ($Expected.valid -ne $Actual.valid -or $Expected.covered -ne $Actual.covered)) {
        throw $script:FcNativeMergeCounts.Invalid
    }
}
