function Read-FcHashFile([string] $Path, [long] $MaximumBytes, [int] $BufferBytes, [string] $Failure) {
    if (-not [IO.File]::Exists($Path)) { throw $Failure }
    $before = Get-Item -LiteralPath $Path -Force
    if ($before -isnot [IO.FileInfo] -or $before.Length -le 0 -or $before.Length -gt $MaximumBytes -or
        ($before.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw $Failure }
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $digest = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $failureFound = $null
    $bytesRead = 0L
    $hash = $null
    try {
        if ($stream.Length -ne $before.Length) { throw $Failure }
        $buffer = [byte[]]::new($BufferBytes)
        while (($count = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $bytesRead += $count
            if ($bytesRead -gt $MaximumBytes) { throw $Failure }
            $digest.AppendData($buffer, 0, $count)
        }
        $after = Get-Item -LiteralPath $Path -Force
        if ($bytesRead -ne $before.Length -or $stream.Length -ne $before.Length -or
            $after.Length -ne $before.Length -or $after.LastWriteTimeUtc -ne $before.LastWriteTimeUtc -or
            ($after.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw $Failure }
        $hash = [Convert]::ToHexString($digest.GetHashAndReset()).ToLowerInvariant()
    }
    catch [System.Exception] { $failureFound = $_.Exception }
    finally {
        try { $digest.Dispose() }
        catch [System.Exception] {
            if ($null -eq $failureFound) { $failureFound = $_.Exception }
            else { $failureFound = [AggregateException]::new($failureFound, $_.Exception) }
        }
        try { $stream.Dispose() }
        catch [System.Exception] {
            if ($null -eq $failureFound) { $failureFound = $_.Exception }
            else { $failureFound = [AggregateException]::new($failureFound, $_.Exception) }
        }
    }
    if ($null -ne $failureFound) { throw $failureFound }
    [ordered]@{ length = $bytesRead; sha256 = $hash }
}

function Read-FcHashReference([string] $Root, [object] $Reference, [long] $MaximumBytes,
    [int] $MaximumPathCharacters) {
    Assert-FcExactKeys $Reference @('path','length','sha256')
    if (($Reference.length -isnot [long] -and $Reference.length -isnot [int]) -or
        $Reference.length -le 0 -or $Reference.length -gt $MaximumBytes -or
        $Reference.sha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcMergeInput.InvalidEvidence }
    $path = Resolve-FcEvidencePath $Root ([string] $Reference.path) $MaximumPathCharacters
    $read = Read-FcHashFile $path $MaximumBytes $script:FcMergeInput.ReadBufferBytes $script:FcMergeInput.InvalidEvidence
    if ($read.length -ne $Reference.length -or $read.sha256 -cne $Reference.sha256) { throw $script:FcMergeInput.InvalidEvidence }
    if (-not $script:FcMergeInput.ReferenceCatalog.ContainsKey($path)) {
        if ($script:FcMergeInput.ReferenceCatalog.Count -ge $script:FcMergeInput.MaximumFiles -or
            $script:FcMergeInput.ReferenceBytes -gt $script:FcMergeInput.MaximumTotalBytes - $read.length) {
            throw $script:FcMergeInput.InvalidEvidence
        }
        $script:FcMergeInput.ReferenceCatalog.Add($path, [ordered]@{ length = $read.length; sha256 = $read.sha256 })
        $script:FcMergeInput.ReferenceBytes += $read.length
    }
    elseif ($script:FcMergeInput.ReferenceCatalog[$path].length -ne $read.length -or
        $script:FcMergeInput.ReferenceCatalog[$path].sha256 -cne $read.sha256) { throw $script:FcMergeInput.InvalidEvidence }
    [ordered]@{ path = $path; relativePath = [string] $Reference.path; length = $read.length; sha256 = $read.sha256 }
}

function Assert-FcSourceScripts([string] $Repository, [object] $Scripts) {
    if ($Scripts -isnot [array] -or $Scripts.Count -eq 0 -or $Scripts.Count -gt $script:FcMergeInput.MaximumFiles) {
        throw $script:FcMergeInput.InvalidSource
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $script:FcMergeInput.ScriptHashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $directory = Join-Path $Repository 'scripts/Features/CodeQuality'
    foreach ($entry in $Scripts) {
        Assert-FcExactKeys $entry @('name','sha256')
        if ([string]::IsNullOrWhiteSpace($entry.name) -or
            ($entry.name -cnotmatch '\Afunctional-coverage[A-Za-z0-9._-]{0,160}\z') -or
            $entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or -not $seen.Add([string] $entry.name)) {
            throw $script:FcMergeInput.InvalidSource
        }
        $path = Join-Path $directory ([string] $entry.name)
        Assert-FcNoReparsePath $path
        $read = Read-FcHashFile $path $script:FcMergeInput.MaximumFileBytes $script:FcMergeInput.ReadBufferBytes $script:FcMergeInput.InvalidSource
        if ($read.sha256 -cne $entry.sha256) { throw $script:FcMergeInput.InvalidSource }
        $script:FcMergeInput.ScriptHashes.Add([string] $entry.name, [string] $read.sha256)
        if (-not $script:FcMergeInput.ReferenceCatalog.ContainsKey($path)) {
            if ($script:FcMergeInput.ReferenceCatalog.Count -ge $script:FcMergeInput.MaximumFiles -or
                $script:FcMergeInput.ReferenceBytes -gt $script:FcMergeInput.MaximumTotalBytes - $read.length) {
                throw $script:FcMergeInput.InvalidSource
            }
            $script:FcMergeInput.ReferenceCatalog.Add($path, [ordered]@{ length = $read.length; sha256 = $read.sha256 })
            $script:FcMergeInput.ReferenceBytes += $read.length
        }
    }
    $actualNames = @((Get-ChildItem -LiteralPath $directory -File -Force | Where-Object {
        $_.Name.StartsWith('functional-coverage', [StringComparison]::Ordinal)
    } | ForEach-Object Name | Sort-Object))
    $recordedNames = @($seen | Sort-Object)
    if ($actualNames.Count -ne $recordedNames.Count -or ($actualNames -join "`n") -cne ($recordedNames -join "`n")) {
        throw $script:FcMergeInput.InvalidSource
    }
}
