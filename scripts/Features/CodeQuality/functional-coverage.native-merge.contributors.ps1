$script:FcNativeContributors = [ordered]@{
    Suites = @('unit','unit-scalar','recovery','rf3')
    Invalid = 'The canonical native contributor inventory is invalid or incomplete.'
    UnitInventoryName = 'functional-coverage.unit-test-inventory.json'
    UnitGroupCount = 5
    UnitGroupPrefix = 'unit-functional-'
    ScalarUnitGroupPrefix = 'unit-scalar-functional-'
    UnitGroupIdFormat = 'D2'
    UnitRunStatusMaximumBytes = 65536
    UnitFilterPrefix = '/*/*/('
    UnitFilterSuffix = ')/*'
    MaximumUnitFilterCharacters = 4096
    HistoricalInventoryFailure = 'The functional unit inventory has not been reconciled to current native test reports.'
}

function Read-FcNativeContributors([object] $SourceManifest, [object] $Bounds, [string[]] $ModuleRoster) {
    if ($SourceManifest.contributors -isnot [array] -or $SourceManifest.contributors.Count -le 0 -or
        $SourceManifest.contributors.Count -gt $Bounds.maximumFiles -or $ModuleRoster.Count -le 0) {
        throw $script:FcNativeContributors.Invalid
    }
    $allowedModules = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($module in $ModuleRoster) {
        if ($module -cnotmatch '\AKeyLoad\.[A-Za-z0-9.]{1,96}\z' -or -not $allowedModules.Add($module)) {
            throw $script:FcNativeContributors.Invalid
        }
    }
    $bySuite = [Collections.Generic.Dictionary[string, Collections.Generic.List[object]]]::new([StringComparer]::Ordinal)
    foreach ($suite in $script:FcNativeContributors.Suites) {
        $bySuite.Add($suite, [Collections.Generic.List[object]]::new())
    }
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $SourceManifest.contributors) {
        Assert-FcNativeContributorExactKeys $entry @('suite','className','methodName','instanceName','requirements',
            'acceptance','executedModules','operationOutcomeAndState')
        if ($entry.suite -cnotin $script:FcNativeContributors.Suites -or
            [string]::IsNullOrWhiteSpace($entry.className) -or [string]::IsNullOrWhiteSpace($entry.methodName) -or
            [string]::IsNullOrWhiteSpace($entry.instanceName) -or $entry.className.Length -gt 512 -or
            $entry.methodName.Length -gt 512 -or $entry.instanceName.Length -gt 512 -or
            [string]::IsNullOrWhiteSpace($entry.operationOutcomeAndState) -or
            $entry.operationOutcomeAndState.Length -gt 4096 -or
            $entry.requirements -isnot [array] -or $entry.acceptance -isnot [array] -or
            $entry.executedModules -isnot [array] -or $entry.requirements.Count -eq 0 -or
            $entry.acceptance.Count -eq 0 -or $entry.executedModules.Count -eq 0) {
            throw $script:FcNativeContributors.Invalid
        }
        $identity = "$($entry.suite.Length):$($entry.suite)$($entry.className.Length):$($entry.className)" +
            "$($entry.methodName.Length):$($entry.methodName)$($entry.instanceName.Length):$($entry.instanceName)"
        if (-not $identities.Add($identity)) { throw $script:FcNativeContributors.Invalid }
        Assert-FcNativeUniqueLabels $entry.requirements '\AREQ-[A-Z0-9-]{1,64}\z'
        Assert-FcNativeUniqueLabels $entry.acceptance '\AAC-[A-Z0-9-]{1,64}\z'
        Assert-FcNativeUniqueLabels $entry.executedModules '\AKeyLoad\.[A-Za-z0-9.]{1,96}\z'
        foreach ($module in $entry.executedModules) {
            if (-not $allowedModules.Contains([string]$module) -or
                $module -cin @('KeyLoad.AppHost','KeyLoad.Analyzers')) { throw $script:FcNativeContributors.Invalid }
        }
        $bySuite[[string] $entry.suite].Add($entry)
    }
    foreach ($suite in $script:FcNativeContributors.Suites) {
        if ($bySuite[$suite].Count -eq 0) { throw $script:FcNativeContributors.Invalid }
    }
    $bySuite
}

function Assert-FcNativeContributorExactKeys([Collections.IDictionary] $Value, [string[]] $Names) {
    if ($null -eq $Value -or $Value.Count -ne $Names.Count) { throw $script:FcNativeContributors.Invalid }
    foreach ($name in $Names) {
        if (-not $Value.Contains($name)) { throw $script:FcNativeContributors.Invalid }
    }
}

function Assert-FcNativeUniqueLabels([object[]] $Values, [string] $Pattern) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $Values) {
        if ($value -isnot [string] -or $value -cnotmatch $Pattern -or -not $seen.Add($value)) {
            throw $script:FcNativeContributors.Invalid
        }
    }
}

function Assert-FcNativeUnitRunStatus([object] $Status, [object] $Inventory, [string] $SourceRevision,
    [string] $SourceManifestSha256) {
    Assert-FcNativeContributorExactKeys $Status @('schemaVersion','sourceRevision','sourceManifestSha256','runs')
    if (($Status.schemaVersion -isnot [int] -and $Status.schemaVersion -isnot [long]) -or $Status.schemaVersion -ne 1 -or
        $Status.sourceRevision -isnot [string] -or $Status.sourceRevision -cne $SourceRevision -or
        $Status.sourceManifestSha256 -isnot [string] -or $Status.sourceManifestSha256 -cne $SourceManifestSha256 -or
        $Status.runs -isnot [array] -or
        $Status.runs.Count -ne 2 * $script:FcNativeContributors.UnitGroupCount + 2) {
        throw $script:FcNativeContributors.Invalid
    }
    $expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $expected.Add('unit-census', [ordered]@{ suite = 'unit'; filter = ''; coverageEnabled = $false })
    $expected.Add('unit-scalar-census', [ordered]@{ suite = 'unit-scalar'; filter = ''; coverageEnabled = $false })
    foreach ($prefix in @($script:FcNativeContributors.UnitGroupPrefix, $script:FcNativeContributors.ScalarUnitGroupPrefix)) {
        for ($index = 1; $index -le $script:FcNativeContributors.UnitGroupCount; $index++) {
            $id = $prefix + $index.ToString($script:FcNativeContributors.UnitGroupIdFormat,
                [Globalization.CultureInfo]::InvariantCulture)
            $suiteName = if ($prefix -ceq $script:FcNativeContributors.UnitGroupPrefix) { 'unit' } else { 'unit-scalar' }
            $expected.Add($id, [ordered]@{ suite = $suiteName
                filter = [string] $Inventory.coverageGroups[$index - 1].selector; coverageEnabled = $true })
        }
    }
    $observed = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($run in $Status.runs) {
        Assert-FcNativeContributorExactKeys $run @('id','suite','filter','coverageEnabled','exitCode','resultsDirectory')
        if ($run.id -isnot [string] -or -not $expected.ContainsKey($run.id) -or $observed.ContainsKey($run.id) -or
            $run.suite -isnot [string] -or $run.suite -cne $expected[$run.id].suite -or
            $run.filter -isnot [string] -or $run.filter -cne $expected[$run.id].filter -or
            $run.coverageEnabled -isnot [bool] -or $run.coverageEnabled -ne $expected[$run.id].coverageEnabled -or
            ($run.exitCode -isnot [int] -and $run.exitCode -isnot [long]) -or $run.exitCode -ne 0 -or
            $run.resultsDirectory -isnot [string] -or $run.resultsDirectory -cne $run.id) {
            throw $script:FcNativeContributors.Invalid
        }
        $observed.Add([string] $run.id, $run)
    }
    if ($observed.Count -ne $expected.Count) { throw $script:FcNativeContributors.Invalid }
    $observed
}

function Assert-FcNativeUnitContributorSubset([object] $Inventory, [object] $Contributors) {
    $cases = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($case in $Inventory.functionalCases) {
        $cases.Add((Get-FcNativeTrxKey $case.className $case.methodName $case.instanceName), $case)
    }
    foreach ($entry in $Contributors['unit']) {
        $identity = Get-FcNativeTrxKey $entry.className $entry.methodName $entry.instanceName
        if (-not $cases.ContainsKey($identity)) { throw $script:FcNativeContributors.Invalid }
        $case = $cases[$identity]
        foreach ($label in $entry.requirements) { if ($label -cnotin $case.requirements) { throw $script:FcNativeContributors.Invalid } }
        foreach ($label in $entry.acceptance) { if ($label -cnotin $case.acceptance) { throw $script:FcNativeContributors.Invalid } }
    }
}
