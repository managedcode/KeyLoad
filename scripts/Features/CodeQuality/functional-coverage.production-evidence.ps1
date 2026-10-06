function Publish-PsmEvidenceCreateOnly([string] $EvidenceRoot, [string] $ManifestPath,
    [byte[]] $ManifestBytes, [Collections.IDictionary] $Files) {
    $artifactBytes = [long]$ManifestBytes.Length
    foreach ($bytes in $Files.Values) {
        if ($artifactBytes -gt ($script:Psm.MaximumManifestBytes - $bytes.Length)) { throw $script:Psm.InvalidReceipt }
        $artifactBytes += $bytes.Length
    }
    if ([IO.File]::Exists($ManifestPath) -or [IO.Directory]::Exists($ManifestPath)) { throw $script:Psm.InvalidReceipt }
    foreach ($name in $Files.Keys) {
        $imagePath = Join-Path $EvidenceRoot $name
        if ([IO.File]::Exists($imagePath) -or [IO.Directory]::Exists($imagePath)) { throw $script:Psm.InvalidReceipt }
    }
    foreach ($name in $Files.Keys) { Write-PsmCreateOnly (Join-Path $EvidenceRoot $name) $Files[$name] }
    Write-PsmCreateOnly $ManifestPath $ManifestBytes
}

function Assert-PsmEvidenceMatches([string] $EvidenceRoot, [string] $ManifestPath,
    [byte[]] $ManifestBytes, [Collections.IDictionary] $Files) {
    foreach ($name in $Files.Keys) {
        $path = Join-Path $EvidenceRoot $name
        if (-not [IO.File]::Exists($path) -or -not (Test-PsmBytesEqual (Read-PsmBounded $path) $Files[$name])) {
            throw $script:Psm.InvalidReceipt
        }
    }
    if (-not [IO.File]::Exists($ManifestPath) -or
        -not (Test-PsmBytesEqual (Read-PsmBounded $ManifestPath) $ManifestBytes)) {
        throw $script:Psm.InvalidReceipt
    }
}
