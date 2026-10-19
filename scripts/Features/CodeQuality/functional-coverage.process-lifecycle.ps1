param([string] $Repository,
    [string] $ResultsDirectory,
    [string] $SourceManifestPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-OriginalJson([string] $Path, [long] $MaximumBytes) {
    $current = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($current)) {
        $entry = Get-Item -LiteralPath $current -Force
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A execution process evidence path is linked.' }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    $file = Get-Item -LiteralPath $Path -Force
    if ($file -isnot [IO.FileInfo] -or $file.Length -le 0 -or $file.Length -gt $MaximumBytes -or
        ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'A execution lifecycle original is absent, linked or outside its size bound.'
    }
    $length = $file.Length
    $modified = $file.LastWriteTimeUtc
    $text = [IO.File]::ReadAllText($Path)
    $after = Get-Item -LiteralPath $Path -Force
    if ($after.Length -ne $length -or $after.LastWriteTimeUtc -ne $modified -or
        ($after.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A execution lifecycle original changed during admission.' }
    $document = [Text.Json.JsonDocument]::Parse($text)
    try { Assert-FcProcessJsonProperties $document.RootElement 0 }
    finally { $document.Dispose() }
    $text | ConvertFrom-Json -AsHashtable -DateKind String -Depth 32
}

function Assert-FcProcessJsonProperties([Text.Json.JsonElement] $Value, [int] $Depth) {
    if ($Depth -gt 32) { throw 'A execution lifecycle original exceeds its JSON depth bound.' }
    if ($Value.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Value.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'A execution lifecycle original repeats a JSON property.' }
            Assert-FcProcessJsonProperties $property.Value ($Depth + 1)
        }
    }
    elseif ($Value.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Value.EnumerateArray()) { Assert-FcProcessJsonProperties $item ($Depth + 1) }
    }
}

function Assert-FcProcessKeys([object] $Value, [string[]] $Expected) {
    if ($Value -isnot [Collections.IDictionary]) { throw 'A execution process original must be a JSON object.' }
    $actual = [Collections.Generic.HashSet[string]]::new([string[]] $Value.Keys, [StringComparer]::Ordinal)
    if ($Value.Count -ne $Expected.Count -or -not $actual.SetEquals($Expected)) {
        throw 'A execution process original has an unexpected field inventory.'
    }
}

function Assert-ProcessIdentity([object] $Process, [object] $Server, [string] $Composition) {
    Assert-FcProcessKeys $Process @('Name','ProcessId','StartedAtUtc','AspireStartedAtUtc','Binary')
    Assert-FcProcessKeys $Process.Binary @('ExecutablePath','ExecutableSha256','AssemblyPath','AssemblySha256',
        'PdbPath','PdbSha256','SourceCount','SourceCompositionSha256')
    if ($Process.ProcessId -isnot [int] -and $Process.ProcessId -isnot [long] -or $Process.ProcessId -le 0 -or
        [string]::IsNullOrWhiteSpace($Process.Name) -or
        [string]::IsNullOrWhiteSpace($Process.StartedAtUtc) -or
        [string]::IsNullOrWhiteSpace($Process.AspireStartedAtUtc)) {
        throw 'The original execution server process identity is incomplete.'
    }
    $started = [DateTimeOffset]::Parse([string] $Process.StartedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
    $aspire = [DateTimeOffset]::Parse([string] $Process.AspireStartedAtUtc, [Globalization.CultureInfo]::InvariantCulture)
    if ($started.Offset -ne [TimeSpan]::Zero -or $aspire.Offset -ne [TimeSpan]::Zero) {
        throw 'The execution process start identities must be actual UTC observations.'
    }
    $binary = $Process.Binary
    if ($binary.AssemblySha256 -cne $Server.dllSha256 -or $binary.PdbSha256 -cne $Server.pdbSha256 -or
        [IO.Path]::GetFileName([string] $binary.AssemblyPath) -cne 'KeyLoad.dll' -or
        [IO.Path]::GetFileName([string] $binary.PdbPath) -cne 'KeyLoad.pdb' -or
        $binary.ExecutableSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $binary.SourceCompositionSha256 -cne $Composition -or $binary.SourceCount -ne $Server.compileReceipt.sourceCount) {
        throw 'The execution server files differ from the original compiled source manifest.'
    }
}

function Get-FcProcessSourceComposition([object] $Server) {
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
    $composition = Get-FcProcessSourceComposition $Server
    $identity = $Server.compiledIdentity
    $startup = @(Read-OriginalJson (Join-Path $Directory 'execution-process-startup.json') 1048576)
    $before = @(Read-OriginalJson (Join-Path $Directory 'execution-process-before-shutdown.json') 1048576)
    $shutdown = Read-OriginalJson (Join-Path $Directory 'execution-process-shutdown.json') 1048576
    Assert-FcProcessKeys $shutdown @('JoinedAtUtc','Processes','Resources')
    if ($startup.Count -eq 0 -or $before.Count -ne $startup.Count -or
        $shutdown.Processes.Count -ne $before.Count -or $shutdown.Resources.Count -ne $before.Count -or
        [string]::IsNullOrWhiteSpace($shutdown.JoinedAtUtc)) {
        throw 'Every observed execution server requires original startup and joined shutdown evidence.'
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($process in $startup) {
        Assert-ProcessIdentity $process $identity $composition
        if (-not $names.Add([string] $process.Name)) { throw 'A execution startup resource is duplicated.' }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($process in $before) {
        Assert-ProcessIdentity $process $identity $composition
        if (-not $names.Contains([string] $process.Name) -or -not $seen.Add([string] $process.Name)) {
            throw 'The execution shutdown resource differs from the observed startup resources.'
        }
        $retained = @($shutdown.Processes | Where-Object Name -CEQ $process.Name)
        $resources = @($shutdown.Resources | Where-Object Name -CEQ $process.Name)
        if ($retained.Count -ne 1 -or $resources.Count -ne 1 -or
            ($retained[0] | ConvertTo-Json -Depth 8 -Compress) -cne ($process | ConvertTo-Json -Depth 8 -Compress)) {
            throw 'The execution shutdown changed the original process evidence.'
        }
        $resource = $resources[0]
        Assert-FcProcessKeys $resource @('Name','ProcessId','StartedAtUtc','AspireStartedAtUtc','ExitCode',
            'StartTimeStamp','StopTimeStamp','State')
        if ($resource.ProcessId -ne $process.ProcessId -or $resource.StartedAtUtc -cne $process.StartedAtUtc -or
            $resource.AspireStartedAtUtc -cne $process.AspireStartedAtUtc -or
            $resource.ExitCode -isnot [int] -and $resource.ExitCode -isnot [long] -or
            [string]::IsNullOrWhiteSpace($resource.StopTimeStamp) -or
            $resource.State -cin @('Running','Starting','Waiting')) {
            throw 'The execution server did not retain an actual completed resource observation.'
        }
    }
    $before.Count
}

function Get-FcProcessLifecycleEvidence([string] $Repository, [string] $ResultsDirectory,
    [string] $SourceManifestPath) {
    $root = [IO.Path]::GetFullPath($Repository)
    $results = [IO.Path]::GetFullPath($ResultsDirectory)
    $relative = [IO.Path]::GetRelativePath($root, $results)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
        $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'The execution lifecycle evidence must belong to this checkout.'
    }
    $manifest = Read-OriginalJson $SourceManifestPath 16777216
    $servers = @($manifest.compiledProducts | Where-Object module -CEQ 'KeyLoad')
    if ($servers.Count -ne 1 -or $manifest.repository -cne 'managedcode/KeyLoad' -or
        $servers[0].compiledIdentity.inspectedSourceRoot -cne $root) {
        throw 'Exactly one original execution server compilation identity is required.'
    }
    $originals = @(Get-ChildItem -LiteralPath $results -Filter 'execution-process-startup.json' -Recurse -File)
    if ($originals.Count -eq 0 -or $originals.Count -gt 4096) { throw 'Execution lifecycle originals are missing or exceed their bound.' }
    $processes = 0
    $files = [Collections.Generic.List[object]]::new()
    foreach ($original in $originals) {
        $processes += Assert-JoinedLifecycle $original.DirectoryName $servers[0]
        foreach ($name in @('execution-process-startup.json','execution-process-before-shutdown.json','execution-process-shutdown.json')) {
            $file = Get-Item -LiteralPath (Join-Path $original.DirectoryName $name)
            $files.Add([ordered]@{ path = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
                length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
        }
    }
    [ordered]@{ sourceRevision = $manifest.sourceRevision; processCount = $processes; originals = $files.ToArray() }
}

if ($MyInvocation.InvocationName -cne '.') {
    Get-FcProcessLifecycleEvidence $Repository $ResultsDirectory $SourceManifestPath |
        ConvertTo-Json -Depth 8 -Compress
}
