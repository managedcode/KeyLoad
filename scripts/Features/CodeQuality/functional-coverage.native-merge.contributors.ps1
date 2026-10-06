$script:FcNativeContributors = [ordered]@{
    Suites = @('unit','unit-scalar','recovery','rf3')
    Invalid = 'The canonical native contributor inventory is invalid or incomplete.'
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
