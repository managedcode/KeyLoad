function Test-FcProductionSnapshotBytesEqual([byte[]] $Left, [byte[]] $Right) {
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) { return $false }
    }
    return $true
}

function Assert-FcProductionSnapshotEqual([byte[]] $OriginalManifestBytes, [byte[]] $CurrentManifestBytes,
    [Collections.Generic.Dictionary[string, byte[]]] $OriginalFiles,
    [Collections.Generic.Dictionary[string, byte[]]] $CurrentFiles) {
    if (-not (Test-FcProductionSnapshotBytesEqual $OriginalManifestBytes $CurrentManifestBytes) -or
        $OriginalFiles.Count -ne $CurrentFiles.Count) {
        throw [InvalidOperationException]::new('The production identity cohort changed during capture.')
    }
    foreach ($name in $OriginalFiles.Keys) {
        if (-not $CurrentFiles.ContainsKey($name) -or
            -not (Test-FcProductionSnapshotBytesEqual $OriginalFiles[$name] $CurrentFiles[$name])) {
            throw [InvalidOperationException]::new('The production identity cohort changed during capture.')
        }
    }
}

function Assert-FcScriptInventoryUnchanged([object[]] $Before, [object[]] $After) {
    if ($Before.Count -ne $After.Count) {
        throw [InvalidOperationException]::new('The production identity cohort changed during capture.')
    }
    for ($index = 0; $index -lt $Before.Count; $index++) {
        if ([string]$Before[$index].name -cne [string]$After[$index].name -or
            [string]$Before[$index].sha256 -cne [string]$After[$index].sha256) {
            throw [InvalidOperationException]::new('The production identity cohort changed during capture.')
        }
    }
}
