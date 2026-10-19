function Read-FcUnitInventory([string] $Root, [object] $SourceManifest, [object] $Bounds,
    [object] $Images, [object] $Contributors) {
    $name = $script:FcContributors.UnitInventoryName
    $matches = @($SourceManifest.scripts | Where-Object { $_.name -ceq $name })
    if ($matches.Count -ne 1 -or $matches[0].sha256 -cnotmatch '\A[0-9a-f]{64}\z') {
        throw $script:FcContributors.HistoricalInventoryFailure
    }
    $relative = 'scripts/Features/CodeQuality/' + $name
    $path = Resolve-FcPath $Root $relative
    $read = Read-FcBoundedFile $path $Bounds.maximumManifestBytes $Bounds.readBufferBytes `
        $script:FcContributors.HistoricalInventoryFailure
    if ($read.sha256 -cne $matches[0].sha256) { throw $script:FcContributors.HistoricalInventoryFailure }
    $document = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($read.bytes))
    try {
        Assert-FcJsonUnique $document.RootElement
        $inventory = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($read.bytes)) -AsHashtable `
            -Depth $script:FcMergeInput.MaximumJsonDepth
    }
    finally { $document.Dispose() }
    Assert-FcUnitInventoryShape $Root $inventory $Images $Bounds
    Assert-FcUnitContributorSubset $inventory $Contributors
    [ordered]@{ value = $inventory; bytes = $read.bytes; length = $read.length; sha256 = $read.sha256 }
}

function Assert-FcUnitInventoryShape([string] $Root, [object] $Inventory, [object] $Images, [object] $Bounds) {
    Assert-FcContributorExactKeys $Inventory @('schemaVersion','inventoryStatus','functionalCases',
        'requiredNonContributorCases','excludedUnitCases','coverageGroups','movedBenchmarkCases')
    if (($Inventory.schemaVersion -isnot [int] -and $Inventory.schemaVersion -isnot [long]) -or $Inventory.schemaVersion -ne 3 -or $Inventory.inventoryStatus -cne 'current-reviewed' -or
        $Inventory.functionalCases -isnot [array] -or $Inventory.functionalCases.Count -eq 0 -or
        $Inventory.excludedUnitCases -isnot [array] -or $Inventory.excludedUnitCases.Count -ne 0 -or
        $Inventory.coverageGroups -isnot [array] -or
        $Inventory.coverageGroups.Count -ne $script:FcContributors.UnitGroupCount -or
        $Inventory.movedBenchmarkCases -isnot [array] -or $Inventory.requiredNonContributorCases -isnot [array]) {
        throw $script:FcContributors.HistoricalInventoryFailure
    }
    $unitSources = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Images['unit'].identity.manifest.sources) {
        $unitSources.Add([string] $source.path, [string] $source.sha256)
    }
    $functional = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $functionalClasses = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $functionalLeaves = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sourceLineCounts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($case in $Inventory.functionalCases) {
        if ($case.classification -cne 'functional') { throw $script:FcContributors.Invalid }
    }
    Assert-FcRequiredNonContributors $Inventory
    foreach ($case in @($Inventory.functionalCases) + @($Inventory.requiredNonContributorCases)) {
        Assert-FcUnitCaseSource $Root $case $unitSources $sourceLineCounts $Bounds
        $identity = Get-FcTrxKey $case.className $case.methodName $case.instanceName
        if ($functional.ContainsKey($identity)) { throw $script:FcContributors.Invalid }
        $functional.Add($identity, $case)
        Assert-FcUniqueLabels $case.requirements '\AREQ-[A-Z0-9-]{1,64}\z'
        Assert-FcUniqueLabels $case.acceptance '\AAC-[A-Z0-9-]{1,64}\z'
        if ($case.requirements.Count -eq 0 -or $case.acceptance.Count -eq 0) {
            throw $script:FcContributors.Invalid
        }
        if ($case.classification -cne 'functional') { continue }
        $newClass = $functionalClasses.Add([string] $case.className)
        $parts = ([string] $case.className).Split('.')
        if ($newClass -and -not $functionalLeaves.Add($parts[$parts.Length - 1])) { throw $script:FcContributors.Invalid }
    }
    Assert-FcMovedBenchmarkCases $Root $Inventory.movedBenchmarkCases $functionalClasses
    $functionalOnly = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $functional.GetEnumerator()) {
        if ($entry.Value.classification -ceq 'functional') { $functionalOnly.Add($entry.Key, $entry.Value) }
    }
    Assert-FcUnitSelectorGroups $Inventory.coverageGroups $functionalOnly $functionalClasses $Inventory
}

function Get-FcSourceLineCount([string] $Root, [string] $RelativePath, [long] $MaximumBytes) {
    $path = Resolve-FcPath $Root $RelativePath
    $before = Get-Item -LiteralPath $path -Force
    if ($before -isnot [IO.FileInfo] -or $before.Length -le 0 -or $before.Length -gt $MaximumBytes -or
        ($before.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw $script:FcContributors.Invalid
    }
    $stream = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, $script:FcMergeInput.ReadBufferBytes, $false)
    $lineCount = 0
    try {
        while ($null -ne $reader.ReadLine()) { $lineCount++ }
        $after = Get-Item -LiteralPath $path -Force
        if ($stream.Length -ne $before.Length -or $after.Length -ne $before.Length -or
            $after.LastWriteTimeUtc -ne $before.LastWriteTimeUtc) {
            throw $script:FcContributors.Invalid
        }
    }
    finally { $reader.Dispose() }
    $lineCount
}

function Assert-FcUnitCaseShape([Collections.IDictionary] $Case, [bool] $Functional = $true) {
    $keys = @('className','methodName','instanceName','sourcePath','sourceSha256',
        'lineNumber','endLineNumber','classification','exclusionReason','requirements','acceptance')
    if ($Functional) { $keys += 'reportClassName' }
    Assert-FcContributorExactKeys $Case $keys
    if ($Functional -and ($Case.reportClassName -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.reportClassName) -or
        $Case.reportClassName.Length -gt 512 -or $Case.reportClassName.IndexOfAny([char[]]@('|', "`r", "`n", [char]0)) -ge 0)) {
        throw $script:FcContributors.Invalid
    }
    if ($Case.className -isnot [string] -or $Case.className.Length -gt 512 -or
        $Case.methodName -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.methodName) -or $Case.methodName.Length -gt 512 -or
        $Case.instanceName -isnot [string] -or [string]::IsNullOrWhiteSpace($Case.instanceName) -or $Case.instanceName.Length -gt 512 -or
        $Case.sourcePath -isnot [string] -or $Case.sourcePath.Length -gt 4096 -or
        $Case.sourcePath.Contains('\\', [StringComparison]::Ordinal) -or [IO.Path]::IsPathRooted($Case.sourcePath) -or
        $Case.sourcePath.Split('/') -contains '..' -or $Case.sourcePath.Split('/') -contains '.' -or
        $Case.sourceSha256 -isnot [string] -or $Case.sourceSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        ($Case.lineNumber -isnot [int] -and $Case.lineNumber -isnot [long]) -or
        ($Case.endLineNumber -isnot [int] -and $Case.endLineNumber -isnot [long]) -or
        $Case.lineNumber -le 0 -or $Case.endLineNumber -lt $Case.lineNumber -or
        $Case.classification -isnot [string] -or $Case.requirements -isnot [array] -or $Case.acceptance -isnot [array]) {
        throw $script:FcContributors.Invalid
    }
}

function Assert-FcMovedBenchmarkCases([string] $Root, [object[]] $Cases,
    [Collections.Generic.HashSet[string]] $FunctionalClasses) {
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sourceHashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($case in $Cases) {
        Assert-FcUnitCaseShape $case $false
        $identity = Get-FcTrxKey $case.className $case.methodName $case.instanceName
        if ($case.className -cnotmatch '\AKeyLoad\.ComparisonTests\.(?:[A-Za-z0-9_]+\.)*[A-Za-z0-9_]+\z' -or
            $case.sourcePath -cnotmatch '\Atests/KeyLoad\.ComparisonTests/(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.cs\z' -or
            $case.classification -cnotin @('comparison','load','stress','performance','progress','entry','counter','image-preparation') -or
            [string]::IsNullOrWhiteSpace([string] $case.exclusionReason) -or
            $case.requirements.Count -ne 0 -or $case.acceptance.Count -ne 0 -or
            $FunctionalClasses.Contains([string] $case.className) -or -not $identities.Add($identity)) {
            throw $script:FcContributors.Invalid
        }
        if (-not $sourceHashes.ContainsKey([string] $case.sourcePath)) {
            $path = Resolve-FcPath $Root ([string] $case.sourcePath)
            if ((Get-FcHash $path) -cne $case.sourceSha256) { throw $script:FcContributors.Invalid }
            $sourceHashes.Add([string] $case.sourcePath, [string] $case.sourceSha256)
        }
        elseif ($sourceHashes[[string] $case.sourcePath] -cne $case.sourceSha256) {
            throw $script:FcContributors.Invalid
        }
    }
}

function Assert-FcUnitSelectorGroups([object[]] $Groups,
    [Collections.Generic.Dictionary[string, object]] $FunctionalCases,
    [Collections.Generic.HashSet[string]] $FunctionalClasses, [object] $Inventory) {
    $assigned = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $assignedClasses = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($index = 0; $index -lt $script:FcContributors.UnitGroupCount; $index++) {
        $group = $Groups[$index]
        Assert-FcContributorExactKeys $group @('groupId','selector','classNames','excludedMethodNames','caseIdentities')
        $expectedId = $script:FcContributors.UnitGroupPrefix + ($index + 1).ToString(
            $script:FcContributors.UnitGroupIdFormat, [Globalization.CultureInfo]::InvariantCulture)
        if ($group.groupId -cne $expectedId -or $group.classNames -isnot [array] -or
            $group.classNames.Count -eq 0 -or $group.caseIdentities -isnot [array] -or
            $group.caseIdentities.Count -eq 0 -or $group.selector -isnot [string]) {
            throw $script:FcContributors.Invalid
        }
        $names = [string[]] @($group.classNames)
        $sortedNames = [string[]] @($names)
        [Array]::Sort($sortedNames, [StringComparer]::Ordinal)
        if (($names -join "`n") -cne ($sortedNames -join "`n")) {
            throw $script:FcContributors.Invalid
        }
        $leaves = [Collections.Generic.List[string]]::new()
        foreach ($className in $names) {
            $parts = $className.Split('.')
            if (-not $FunctionalClasses.Contains($className) -or -not $assignedClasses.Add($className)) {
                throw $script:FcContributors.Invalid
            }
            $leaves.Add($parts[$parts.Length - 1])
        }
        $caseIdentities = [string[]] @($group.caseIdentities)
        $sortedIdentities = [string[]] @($caseIdentities)
        [Array]::Sort($sortedIdentities, [StringComparer]::Ordinal)
        if (($caseIdentities -join "`n") -cne ($sortedIdentities -join "`n")) {
            throw $script:FcContributors.Invalid
        }
        foreach ($identity in $caseIdentities) {
            if (-not $FunctionalCases.ContainsKey($identity) -or -not $assigned.Add($identity) -or
                $FunctionalCases[$identity].className -cnotin $names) { throw $script:FcContributors.Invalid }
        }
        $groupCases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($identity in $FunctionalCases.Keys) {
            if ($FunctionalCases[$identity].className -cin $names) { [void] $groupCases.Add($identity) }
        }
        if (-not $groupCases.SetEquals($caseIdentities)) { throw $script:FcContributors.Invalid }
        $selector = Get-FcExactUnitSelector $group $Inventory $leaves.ToArray()
        if ($group.selector -cne $selector -or $selector.Length -gt $script:FcContributors.MaximumUnitFilterCharacters) {
            throw $script:FcContributors.Invalid
        }
    }
    if (-not $assigned.SetEquals([string[]] $FunctionalCases.Keys) -or
        -not $assignedClasses.SetEquals([string[]] $FunctionalClasses)) { throw $script:FcContributors.Invalid }
}

function Get-FcBaseSuite([string] $Suite) {
    if ($Suite -cin $script:FcContributors.Suites) { return $Suite }
    foreach ($prefix in @($script:FcContributors.UnitGroupPrefix)) {
        if ($Suite.StartsWith($prefix, [StringComparison]::Ordinal)) {
            $suffix = $Suite.Substring($prefix.Length)
            if ($suffix -cmatch '\A0[1-5]\z') { return 'unit' }
        }
    }
    ''
}

function Get-FcExpectedSuiteCases([string] $Suite, [object] $Contributors, [object] $Inventory) {
    $base = Get-FcBaseSuite $Suite
    if ($base -cin @('recovery','cluster')) { return @($Contributors[$base]) }
    if ($base -cne 'unit') { throw $script:FcContributors.Invalid }
    $prefix = $script:FcContributors.UnitGroupPrefix
    if (-not $Suite.StartsWith($prefix, [StringComparison]::Ordinal)) {
        throw $script:FcContributors.Invalid
    }
    $index = [int]::Parse($Suite.Substring($prefix.Length), [Globalization.CultureInfo]::InvariantCulture) - 1
    $group = $Inventory.coverageGroups[$index]
    $inventoryCases = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($inventoryCase in $Inventory.functionalCases) {
        $inventoryCases.Add((Get-FcTrxKey $inventoryCase.className $inventoryCase.methodName $inventoryCase.instanceName),
            $inventoryCase)
    }
    $cases = [Collections.Generic.List[object]]::new()
    foreach ($identity in $group.caseIdentities) {
        if (-not $inventoryCases.ContainsKey([string] $identity)) { throw $script:FcContributors.Invalid }
        $cases.Add($inventoryCases[[string] $identity])
    }
    @($cases.ToArray())
}


function Assert-FcUnitCaseSource([string] $Root, [object] $Case, [object] $UnitSources,
    [object] $SourceLineCounts, [object] $Bounds) {
        if ($Case.classification -ceq 'required-non-contributor') {
            $canonical = [ordered]@{}
            foreach ($key in $Case.Keys) {
                if ($key -cnotin @('nonContributorReason','reviewBasis')) { $canonical[$key] = $Case[$key] }
            }
            Assert-FcUnitCaseShape $canonical
        }
        else { Assert-FcUnitCaseShape $Case }
        if ($Case.className -cnotmatch '\AKeyLoad\.UnitTests\.(?:[A-Za-z0-9_]+\.)*[A-Za-z0-9_]+\z' -or
            $Case.classification -cnotin @('functional','required-non-contributor') -or
            ($Case.exclusionReason -isnot [System.DBNull] -and $null -ne $Case.exclusionReason) -or
            -not $UnitSources.ContainsKey([string] $Case.sourcePath) -or
            $UnitSources[[string] $Case.sourcePath] -cne $Case.sourceSha256) {
            throw $script:FcContributors.Invalid
        }
        $sourcePath = [string] $Case.sourcePath
        if (-not $SourceLineCounts.ContainsKey($sourcePath)) {
            $SourceLineCounts.Add($sourcePath, (Get-FcSourceLineCount $Root $sourcePath $Bounds.maximumFileBytes))
        }
        if ($Case.endLineNumber -gt $SourceLineCounts[$sourcePath]) {
            throw $script:FcContributors.Invalid
        }
}
