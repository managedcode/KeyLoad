function Assert-FcNativeRequiredNonContributors([object] $Inventory) {
    foreach ($case in $Inventory.requiredNonContributorCases) {
        Assert-FcNativeContributorExactKeys $case @('className','nativeReportClassName','methodName','instanceName',
            'sourcePath','sourceSha256','lineNumber','endLineNumber','classification','exclusionReason',
            'requirements','acceptance','nonContributorReason','reviewBasis')
        if ($case.classification -cne 'required-non-contributor') { throw $script:FcNativeContributors.Invalid }
        foreach ($name in @('nonContributorReason','reviewBasis')) {
            if ($case[$name] -isnot [string] -or [string]::IsNullOrWhiteSpace($case[$name]) -or
                $case[$name].Length -gt 4096) { throw $script:FcNativeContributors.Invalid }
        }
    }
}

function Get-FcNativeExactUnitSelector([object] $Group, [object] $Inventory, [string[]] $Leaves) {
    if ($Group.excludedMethodNames -isnot [array]) { throw $script:FcNativeContributors.Invalid }
    $excluded = [string[]] @($Inventory.requiredNonContributorCases | Where-Object {
        $_.className -cin $Group.classNames
    } | ForEach-Object { [string] $_.methodName } | Sort-Object -Unique)
    if (($excluded -join "`n") -cne ($Group.excludedMethodNames -join "`n")) {
        throw $script:FcNativeContributors.Invalid
    }
    foreach ($name in @($Leaves) + @($excluded)) {
        if ($name -cnotmatch '\A[A-Za-z_][A-Za-z0-9_]*\z') { throw $script:FcNativeContributors.Invalid }
    }
    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in @($Inventory.functionalCases) + @($Inventory.requiredNonContributorCases)) {
        $leaf = ([string] $case.className).Split('.')[-1]
        if ($leaf -cin $Leaves -and $case.className -cnotin $Group.classNames) {
            throw $script:FcNativeContributors.Invalid
        }
        if ($case.className -cnotin $Group.classNames) { continue }
        $denied = $false
        foreach ($method in $excluded) {
            if ($case.methodName.StartsWith($method, [StringComparison]::Ordinal)) { $denied = $true; break }
        }
        if (-not $denied) {
            if ($case.classification -cne 'functional') { throw $script:FcNativeContributors.Invalid }
            [void] $selected.Add((Get-FcNativeTrxKey $case.className $case.methodName $case.instanceName))
        }
    }
    if (-not $selected.SetEquals([string[]] $Group.caseIdentities)) { throw $script:FcNativeContributors.Invalid }
    $patterns = Get-FcNativeConfinedClassPrefixes $Leaves $Inventory
    $classes = '(' + ($patterns -join ')|(') + ')'
    $methods = if ($excluded.Count -eq 0) { '*' } else { '(*)&(!' + ($excluded -join '*)&(!') + '*)' }
    '/*/*/' + $classes + '/' + $methods
}


function Get-FcNativeConfinedClassPrefixes([string[]] $Leaves, [object] $Inventory) {
    $universe = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($case in @($Inventory.functionalCases) + @($Inventory.requiredNonContributorCases)) {
        $name = [string] $case.className
        $leaf = $name.Split('.')[-1]
        if ($universe.ContainsKey($leaf) -and $universe[$leaf] -cne $name) {
            throw $script:FcNativeContributors.Invalid
        }
        $universe[$leaf] = $name
    }
    $allowed = [Collections.Generic.HashSet[string]]::new([string[]] $Leaves, [StringComparer]::Ordinal)
    if ($allowed.Count -ne $Leaves.Count) { throw $script:FcNativeContributors.Invalid }
    $patterns = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($leaf in $Leaves) {
        if (-not $universe.ContainsKey($leaf)) { throw $script:FcNativeContributors.Invalid }
        $found = $false
        for ($length = 1; $length -le $leaf.Length; $length++) {
            $prefix = $leaf.Substring(0, $length)
            $confined = $true
            foreach ($other in $universe.Keys) {
                if ($other.StartsWith($prefix, [StringComparison]::Ordinal) -and -not $allowed.Contains($other)) {
                    $confined = $false; break
                }
            }
            if ($confined) { [void] $patterns.Add($prefix + '*'); $found = $true; break }
        }
        if (-not $found) { throw $script:FcNativeContributors.Invalid }
    }
    $minimal = [Collections.Generic.List[string]]::new()
    foreach ($pattern in $patterns) {
        $redundant = $false
        foreach ($other in $patterns) {
            if ($pattern -cne $other -and $pattern.StartsWith($other.Substring(0, $other.Length - 1),
                    [StringComparison]::Ordinal)) { $redundant = $true; break }
        }
        if (-not $redundant) { $minimal.Add($pattern) }
    }
    $result = [string[]] $minimal.ToArray()
    [Array]::Sort($result, [StringComparer]::Ordinal)
    $result
}


function Assert-FcNativeObservedCensus([object] $Census, [object] $Inventory, [string] $Repository,
    [string] $AssemblyFullName, [string[]] $ExpectedIdentities) {
    Assert-FcNativeContributorExactKeys $Census @('schemaVersion','tests')
    if (($Census.schemaVersion -isnot [int] -and $Census.schemaVersion -isnot [long]) -or
        $Census.schemaVersion -ne 1 -or $Census.tests -isnot [array]) { throw $script:FcNativeContributors.Invalid }
    $reviewed = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($case in @($Inventory.functionalCases) + @($Inventory.requiredNonContributorCases)) {
        $key = Get-FcNativeTrxKey $case.nativeReportClassName $case.methodName $case.instanceName
        $reviewed.Add($key, $case)
    }
    $uids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($test in $Census.tests) {
        Assert-FcNativeContributorExactKeys $test @('uid','displayName','type','location')
        Assert-FcNativeObservedTestType $test.type $AssemblyFullName
        Assert-FcNativeContributorExactKeys $test.location @('file','lineStart','lineEnd')
        if ($test.uid -isnot [string] -or [string]::IsNullOrWhiteSpace($test.uid) -or
            -not $uids.Add($test.uid) -or $test.displayName -isnot [string]) {
            throw $script:FcNativeContributors.Invalid
        }
        $reported = $test.type.namespace + '.' + $test.type.typeName
        $reportedKey = Get-FcNativeTrxKey $reported $test.type.methodName $test.displayName
        if (-not $reviewed.ContainsKey($reportedKey)) { throw $script:FcNativeContributors.Invalid }
        $case = $reviewed[$reportedKey]
        Assert-FcNativeObservedTestLocation $test.location $case $Repository
        if (-not $test.uid.StartsWith($case.className + '.', [StringComparison]::Ordinal) -and
            -not $test.uid.StartsWith($case.className + '(', [StringComparison]::Ordinal)) {
            throw $script:FcNativeContributors.Invalid
        }
        if (-not $observed.Add((Get-FcNativeTrxKey $case.className $case.methodName $case.instanceName))) {
            throw $script:FcNativeContributors.Invalid
        }
    }
    if (-not $observed.SetEquals($ExpectedIdentities)) { throw $script:FcNativeContributors.Invalid }
}

function Assert-FcNativeObservedTestType([object] $Type, [string] $AssemblyFullName) {
    Assert-FcNativeContributorExactKeys $Type @('assemblyFullName','namespace','typeName','methodName',
        'methodArity','returnTypeFullName','parameterTypeFullNames')
    foreach ($name in @('assemblyFullName','namespace','typeName','methodName','returnTypeFullName')) {
        if ($Type[$name] -isnot [string] -or [string]::IsNullOrWhiteSpace($Type[$name]) -or
            $Type[$name].Length -gt 4096 -or $Type[$name].IndexOfAny([char[]]@([char]0, "`r", "`n")) -ge 0) {
            throw $script:FcNativeContributors.Invalid
        }
    }
    if ($Type.assemblyFullName -cne $AssemblyFullName -or
        ($Type.methodArity -isnot [int] -and $Type.methodArity -isnot [long]) -or
        $Type.methodArity -lt 0 -or $Type.parameterTypeFullNames -isnot [array]) {
        throw $script:FcNativeContributors.Invalid
    }
    foreach ($parameter in $Type.parameterTypeFullNames) {
        if ($parameter -isnot [string] -or [string]::IsNullOrWhiteSpace($parameter) -or
            $parameter.Length -gt 4096) { throw $script:FcNativeContributors.Invalid }
    }
}

function Assert-FcNativeObservedTestLocation([object] $Location, [object] $Case, [string] $Repository) {
    if (($Location.lineStart -isnot [int] -and $Location.lineStart -isnot [long]) -or
        ($Location.lineEnd -isnot [int] -and $Location.lineEnd -isnot [long]) -or
        $Location.lineStart -ne $Case.lineNumber -or $Location.lineEnd -ne $Case.endLineNumber -or
        $Location.file -isnot [string]) { throw $script:FcNativeContributors.Invalid }
    $local = [IO.Path]::Combine($Repository, $Case.sourcePath).Replace('\', '/')
    $canonical = '/_/' + $Case.sourcePath
    if ($Location.file -cne $local -and $Location.file -cne $canonical) {
        throw $script:FcNativeContributors.Invalid
    }
}
