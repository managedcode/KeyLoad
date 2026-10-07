function Get-FcNativeRangeReboundInventory([object] $Inventory, [object] $Census,
    [string] $Repository, [string] $AssemblyFullName, [object] $Images, [object] $Bounds) {
    $copy = ConvertFrom-Json (ConvertTo-Json -InputObject $Inventory -Depth 64) -AsHashtable -Depth 64
    $reviewed = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($case in @($copy.functionalCases) + @($copy.requiredNonContributorCases)) {
        $key = Get-FcNativeTrxKey $case.nativeReportClassName $case.methodName $case.instanceName
        $reviewed.Add($key, $case)
    }
    Assert-FcNativeContributorExactKeys $Census @('schemaVersion','tests')
    if (($Census.schemaVersion -isnot [int] -and $Census.schemaVersion -isnot [long]) -or
        $Census.schemaVersion -ne 1 -or $Census.tests -isnot [array]) { throw $script:FcNativeContributors.Invalid }
    $matched = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($test in $Census.tests) {
        Assert-FcNativeContributorExactKeys $test @('uid','displayName','type','location')
        Assert-FcNativeObservedTestType $test.type $AssemblyFullName
        Assert-FcNativeContributorExactKeys $test.location @('file','lineStart','lineEnd')
        $key = Get-FcNativeTrxKey ($test.type.namespace + '.' + $test.type.typeName) `
            $test.type.methodName $test.displayName
        if (-not $reviewed.ContainsKey($key) -or -not $matched.Add($key)) {
            throw $script:FcNativeContributors.Invalid
        }
        $case = $reviewed[$key]
        Assert-FcNativeReboundLocation $test.location $case $Repository
        $case.lineNumber = $test.location.lineStart
        $case.endLineNumber = $test.location.lineEnd
    }
    if (-not $matched.SetEquals([string[]] $reviewed.Keys)) { throw $script:FcNativeContributors.Invalid }
    Assert-FcNativeUnitInventoryShape $Repository $copy $Images $Bounds
    $expected = [string[]] @(@($copy.functionalCases) + @($copy.requiredNonContributorCases) |
        ForEach-Object { Get-FcNativeTrxKey $_.className $_.methodName $_.instanceName })
    Assert-FcNativeObservedCensus $Census $copy $Repository $AssemblyFullName $expected
    $copy
}

function Assert-FcNativeReboundLocation([object] $Location, [object] $Case, [string] $Repository) {
    if (($Location.lineStart -isnot [int] -and $Location.lineStart -isnot [long]) -or
        ($Location.lineEnd -isnot [int] -and $Location.lineEnd -isnot [long]) -or
        $Location.lineStart -le 0 -or $Location.lineEnd -lt $Location.lineStart -or
        $Location.file -isnot [string]) { throw $script:FcNativeContributors.Invalid }
    $local = [IO.Path]::Combine($Repository, $Case.sourcePath).Replace('\', '/')
    if ($Location.file -cne $local -and $Location.file -cne ('/_/' + $Case.sourcePath)) {
        throw $script:FcNativeContributors.Invalid
    }
}

function Write-FcNativePrivateReboundInventory([object] $Inventory, [string] $Directory, [string] $Repository) {
    $full = [IO.Path]::GetFullPath($Directory)
    $relative = [IO.Path]::GetRelativePath($Repository, $full)
    if ($relative -eq '.' -or (-not [IO.Path]::IsPathRooted($relative) -and
        $relative -ne '..' -and -not $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::Ordinal))) { throw $script:FcNativeContributors.Invalid }
    $anchor = [IO.Path]::GetPathRoot($full)
    $full = Resolve-FcPath $anchor ([IO.Path]::GetRelativePath($anchor, $full))
    $info = Get-Item -LiteralPath $full -Force
    if ($info -isnot [IO.DirectoryInfo] -or ($info.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw $script:FcNativeContributors.Invalid
    }
    $path = Resolve-FcPath $full 'inventory.current-native.v3.json'
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Inventory -Depth 64) + "`n")
    if ($bytes.Length -le 0 -or $bytes.Length -gt 33554432) { throw $script:FcNativeContributors.Invalid }
    $stream = [IO.FileStream]::new($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes); $stream.Flush($true) }
    finally { $stream.Dispose() }
    Write-Output ('Private native range reconciliation written: ' + $path)
}
