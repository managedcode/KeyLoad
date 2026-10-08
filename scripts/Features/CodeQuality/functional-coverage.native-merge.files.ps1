function Read-FcNativeHashFile([string] $Path, [long] $MaximumBytes, [int] $BufferBytes, [string] $Failure) {
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

function Read-FcNativeHashReference([string] $Root, [object] $Reference, [long] $MaximumBytes,
    [int] $MaximumPathCharacters) {
    Assert-FcNativeExactKeys $Reference @('path','length','sha256')
    if (($Reference.length -isnot [long] -and $Reference.length -isnot [int]) -or
        $Reference.length -le 0 -or $Reference.length -gt $MaximumBytes -or
        $Reference.sha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw $script:FcNativeMergeInput.InvalidEvidence }
    $path = Resolve-FcNativeEvidencePath $Root ([string] $Reference.path) $MaximumPathCharacters
    $read = Read-FcNativeHashFile $path $MaximumBytes $script:FcNativeMergeInput.ReadBufferBytes $script:FcNativeMergeInput.InvalidEvidence
    if ($read.length -ne $Reference.length -or $read.sha256 -cne $Reference.sha256) { throw $script:FcNativeMergeInput.InvalidEvidence }
    if (-not $script:FcNativeMergeInput.ReferenceCatalog.ContainsKey($path)) {
        if ($script:FcNativeMergeInput.ReferenceCatalog.Count -ge $script:FcNativeMergeInput.MaximumFiles -or
            $script:FcNativeMergeInput.ReferenceBytes -gt $script:FcNativeMergeInput.MaximumTotalBytes - $read.length) {
            throw $script:FcNativeMergeInput.InvalidEvidence
        }
        $script:FcNativeMergeInput.ReferenceCatalog.Add($path, [ordered]@{ length = $read.length; sha256 = $read.sha256 })
        $script:FcNativeMergeInput.ReferenceBytes += $read.length
    }
    elseif ($script:FcNativeMergeInput.ReferenceCatalog[$path].length -ne $read.length -or
        $script:FcNativeMergeInput.ReferenceCatalog[$path].sha256 -cne $read.sha256) { throw $script:FcNativeMergeInput.InvalidEvidence }
    [ordered]@{ path = $path; relativePath = [string] $Reference.path; length = $read.length; sha256 = $read.sha256 }
}

function Assert-FcNativeSourceScripts([string] $Repository, [object] $Scripts) {
    if ($Scripts -isnot [array] -or $Scripts.Count -eq 0 -or $Scripts.Count -gt $script:FcNativeMergeInput.MaximumFiles) {
        throw $script:FcNativeMergeInput.InvalidSource
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $script:FcNativeMergeInput.ScriptHashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $directory = Join-Path $Repository 'scripts/Features/CodeQuality'
    foreach ($entry in $Scripts) {
        Assert-FcNativeExactKeys $entry @('name','sha256')
        if ([string]::IsNullOrWhiteSpace($entry.name) -or
            (-not (Test-FcTaskAcceptanceScriptName ([string] $entry.name)) -and
                $entry.name -cne '.dockerignore' -and $entry.name -cnotmatch '\Afunctional-coverage[A-Za-z0-9._-]{0,160}\z') -or
            $entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z' -or -not $seen.Add([string] $entry.name)) {
            throw $script:FcNativeMergeInput.InvalidSource
        }
        $path = Join-Path $directory ([string] $entry.name)
        Assert-FcNativeNoReparsePath $path
        $read = Read-FcNativeHashFile $path $script:FcNativeMergeInput.MaximumFileBytes $script:FcNativeMergeInput.ReadBufferBytes $script:FcNativeMergeInput.InvalidSource
        if ($read.sha256 -cne $entry.sha256) { throw $script:FcNativeMergeInput.InvalidSource }
        $script:FcNativeMergeInput.ScriptHashes.Add([string] $entry.name, [string] $read.sha256)
        if (-not $script:FcNativeMergeInput.ReferenceCatalog.ContainsKey($path)) {
            if ($script:FcNativeMergeInput.ReferenceCatalog.Count -ge $script:FcNativeMergeInput.MaximumFiles -or
                $script:FcNativeMergeInput.ReferenceBytes -gt $script:FcNativeMergeInput.MaximumTotalBytes - $read.length) {
                throw $script:FcNativeMergeInput.InvalidSource
            }
            $script:FcNativeMergeInput.ReferenceCatalog.Add($path, [ordered]@{ length = $read.length; sha256 = $read.sha256 })
            $script:FcNativeMergeInput.ReferenceBytes += $read.length
        }
    }
    $actualNames = @((Get-ChildItem -LiteralPath $directory -File -Force | Where-Object {
        (Test-FcTaskAcceptanceScriptName $_.Name) -or
        $_.Name.StartsWith('functional-coverage', [StringComparison]::Ordinal) -or $_.Name -ceq '.dockerignore'
    } | ForEach-Object Name | Sort-Object))
    $recordedNames = @($seen | Sort-Object)
    if ($actualNames.Count -ne $recordedNames.Count -or ($actualNames -join "`n") -cne ($recordedNames -join "`n")) {
        throw $script:FcNativeMergeInput.InvalidSource
    }
}
