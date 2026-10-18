param([string] $Repository,
    [string] $ResultsDirectory,
    [string] $SourceManifestPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-OriginalJson([string] $Path, [long] $MaximumBytes) {
    $current = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($current)) {
        $entry = Get-Item -LiteralPath $current -Force
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A native process evidence path is linked.' }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    $file = Get-Item -LiteralPath $Path -Force
    if ($file -isnot [IO.FileInfo] -or $file.Length -le 0 -or $file.Length -gt $MaximumBytes -or
        ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'A native lifecycle original is absent, linked or outside its size bound.'
    }
    $length = $file.Length
    $modified = $file.LastWriteTimeUtc
    $text = [IO.File]::ReadAllText($Path)
    $after = Get-Item -LiteralPath $Path -Force
    if ($after.Length -ne $length -or $after.LastWriteTimeUtc -ne $modified -or
        ($after.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A native lifecycle original changed during admission.' }
    $document = [Text.Json.JsonDocument]::Parse($text)
    try { Assert-FcNativeProcessJsonProperties $document.RootElement 0 }
    finally { $document.Dispose() }
    $text | ConvertFrom-Json -AsHashtable -DateKind String -Depth 32
}

function Assert-FcNativeProcessJsonProperties([Text.Json.JsonElement] $Value, [int] $Depth) {
    if ($Depth -gt 32) { throw 'A native lifecycle original exceeds its JSON depth bound.' }
    if ($Value.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Value.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'A native lifecycle original repeats a JSON property.' }
            Assert-FcNativeProcessJsonProperties $property.Value ($Depth + 1)
        }
    }
    elseif ($Value.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Value.EnumerateArray()) { Assert-FcNativeProcessJsonProperties $item ($Depth + 1) }
    }
}

function Assert-FcNativeProcessKeys([object] $Value, [string[]] $Expected) {
    if ($Value -isnot [Collections.IDictionary]) { throw 'A native process original must be a JSON object.' }
    $actual = [Collections.Generic.HashSet[string]]::new([string[]] $Value.Keys, [StringComparer]::Ordinal)
    if ($Value.Count -ne $Expected.Count -or -not $actual.SetEquals($Expected)) {
        throw 'A native process original has an unexpected field inventory.'
    }
}

function Assert-ProcessIdentity([object] $Process, [object] $Server, [string] $Composition) {
    Assert-FcNativeProcessKeys $Process @('Name','ProcessId','StartedAtUtc','AspireStartedAtUtc','Binary')
    Assert-FcNativeProcessKeys $Process.Binary @('ExecutablePath','ExecutableSha256','AssemblyPath','AssemblySha256',
        'PdbPath','PdbSha256','SourceCount','SourceCompositionSha256')
    if ($Process.ProcessId -isnot [int] -and $Process.ProcessId -isnot [long] -or $Process.ProcessId -le 0 -or
        [string]::IsNullOrWhiteSpace($Process.Name) -or
        [string]::IsNullOrWhiteSpace($Process.StartedAtUtc) -or
        [string]::IsNullOrWhiteSpace($Process.AspireStartedAtUtc)) {
        throw 'The original native server process identity is incomplete.'
    }
    $started = [DateTimeOffset]::Parse([string] $Process.StartedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
    $aspire = [DateTimeOffset]::Parse([string] $Process.AspireStartedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
    if ($started.Offset -ne [TimeSpan]::Zero -or $aspire.Offset -ne [TimeSpan]::Zero) {
        throw 'The native process start identities must be actual UTC observations.'
    }
    $binary = $Process.Binary
    if ($binary.AssemblySha256 -cne $Server.dllSha256 -or $binary.PdbSha256 -cne $Server.pdbSha256 -or
        [IO.Path]::GetFileName([string] $binary.AssemblyPath) -cne 'KeyLoad.Server.dll' -or
        [IO.Path]::GetFileName([string] $binary.PdbPath) -cne 'KeyLoad.Server.pdb' -or
        $binary.ExecutableSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $binary.SourceCompositionSha256 -cne $Composition -or $binary.SourceCount -ne $Server.compileReceipt.sourceCount) {
        throw 'The native server files differ from the original compiled source manifest.'
    }
}

function Get-FcNativeProcessSourceComposition([object] $Server) {
    $entries = [Collections.Generic.List[string]]::new()
    $prefix = 'KeyLoad.FunctionalCompileIdentity.'
    foreach ($source in $Server.sources) { $entries.Add($prefix + 'Source=' + $source.path + '|' + $source.sha256) }
    foreach ($input in $Server.compiledIdentity.compileReceipt.centralInputs) {
        $entries.Add($prefix + 'Central=' + $input.path + '|' + $input.sha256)
    }
    $producer = $Server.compiledIdentity.compileReceipt.producer
    $entries.Add($prefix + 'Producer=' + $producer.path + '|' + $producer.sha256)
    $ordered = [string[]] $entries.ToArray()
    [Array]::Sort($ordered, [StringComparer]::Ordinal)
    [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($ordered -join "`n"))))
}

function Assert-JoinedLifecycle([string] $Directory, [object] $Server) {
    $composition = Get-FcNativeProcessSourceComposition $Server
    $identity = $Server.compiledIdentity
    $startup = @(Read-OriginalJson (Join-Path $Directory 'native-process-startup.json') 1048576)
    $before = @(Read-OriginalJson (Join-Path $Directory 'native-process-before-shutdown.json') 1048576)
    $shutdown = Read-OriginalJson (Join-Path $Directory 'native-process-shutdown.json') 1048576
    Assert-FcNativeProcessKeys $shutdown @('JoinedAtUtc','Processes','Resources')
    if ($startup.Count -eq 0 -or $before.Count -ne $startup.Count -or
        $shutdown.Processes.Count -ne $before.Count -or $shutdown.Resources.Count -ne $before.Count -or
        [string]::IsNullOrWhiteSpace($shutdown.JoinedAtUtc)) {
        throw 'Every observed native server requires original startup and joined shutdown evidence.'
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($process in $startup) {
        Assert-ProcessIdentity $process $identity $composition
        if (-not $names.Add([string] $process.Name)) { throw 'A native startup resource is duplicated.' }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($process in $before) {
        Assert-ProcessIdentity $process $identity $composition
        if (-not $names.Contains([string] $process.Name) -or -not $seen.Add([string] $process.Name)) {
            throw 'The native shutdown resource differs from the observed startup resources.'
        }
        $retained = @($shutdown.Processes | Where-Object Name -CEQ $process.Name)
        $resources = @($shutdown.Resources | Where-Object Name -CEQ $process.Name)
        if ($retained.Count -ne 1 -or $resources.Count -ne 1 -or
            ($retained[0] | ConvertTo-Json -Depth 8 -Compress) -cne ($process | ConvertTo-Json -Depth 8 -Compress)) {
            throw 'The native shutdown changed the original process evidence.'
        }
        $resource = $resources[0]
        Assert-FcNativeProcessKeys $resource @('Name','ProcessId','StartedAtUtc','AspireStartedAtUtc','ExitCode',
            'StartTimeStamp','StopTimeStamp','State')
        if ($resource.ProcessId -ne $process.ProcessId -or $resource.StartedAtUtc -cne $process.StartedAtUtc -or
            $resource.AspireStartedAtUtc -cne $process.AspireStartedAtUtc -or
            $resource.ExitCode -isnot [int] -and $resource.ExitCode -isnot [long] -or
            [string]::IsNullOrWhiteSpace($resource.StopTimeStamp) -or
            $resource.State -cin @('Running','Starting','Waiting')) {
            throw 'The native server did not retain an actual completed resource observation.'
        }
    }
    $before.Count
}

function Get-FcNativeProcessLifecycleEvidence([string] $Repository, [string] $ResultsDirectory,
    [string] $SourceManifestPath) {
    $root = [IO.Path]::GetFullPath($Repository)
    $results = [IO.Path]::GetFullPath($ResultsDirectory)
    $relative = [IO.Path]::GetRelativePath($root, $results)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
        $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'The native lifecycle evidence must belong to this checkout.'
    }
    $manifest = Read-OriginalJson $SourceManifestPath 16777216
    $servers = @($manifest.compiledProducts | Where-Object module -CEQ 'KeyLoad.Server')
    if ($servers.Count -ne 1 -or $manifest.repository -cne 'managedcode/KeyLoad' -or
        $servers[0].compiledIdentity.inspectedSourceRoot -cne $root) {
        throw 'Exactly one original native server compilation identity is required.'
    }
    $originals = @(Get-ChildItem -LiteralPath $results -Filter 'native-process-startup.json' -Recurse -File)
    if ($originals.Count -eq 0 -or $originals.Count -gt 4096) { throw 'Native lifecycle originals are missing or exceed their bound.' }
    $processes = 0
    $files = [Collections.Generic.List[object]]::new()
    foreach ($original in $originals) {
        $processes += Assert-JoinedLifecycle $original.DirectoryName $servers[0]
        foreach ($name in @('native-process-startup.json','native-process-before-shutdown.json','native-process-shutdown.json')) {
            $file = Get-Item -LiteralPath (Join-Path $original.DirectoryName $name)
            $files.Add([ordered]@{ path = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
                length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
        }
    }
    [ordered]@{ sourceRevision = $manifest.sourceRevision; processCount = $processes; originals = $files.ToArray() }
}

if ($MyInvocation.InvocationName -cne '.') {
    Get-FcNativeProcessLifecycleEvidence $Repository $ResultsDirectory $SourceManifestPath |
        ConvertTo-Json -Depth 8 -Compress
}
