param([Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $ResultsDirectory,
    [Parameter(Mandatory)][string] $SourceManifestPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-OriginalJson([string] $Path, [long] $MaximumBytes) {
    $file = Get-Item -LiteralPath $Path -Force
    if ($file -isnot [IO.FileInfo] -or $file.Length -le 0 -or $file.Length -gt $MaximumBytes -or
        ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'A native lifecycle original is absent, linked or outside its size bound.'
    }
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable -DateKind String -Depth 32
}

function Assert-ProcessIdentity([object] $Process, [object] $Server) {
    if ($Process.ProcessId -isnot [int] -and $Process.ProcessId -isnot [long] -or $Process.ProcessId -le 0 -or
        [string]::IsNullOrWhiteSpace($Process.Name) -or
        [string]::IsNullOrWhiteSpace($Process.StartedAtUtc) -or
        [string]::IsNullOrWhiteSpace($Process.AspireStartedAtUtc)) {
        throw 'The original native server process identity is incomplete.'
    }
    $binary = $Process.Binary
    if ($binary.AssemblySha256 -cne $Server.dllSha256 -or $binary.PdbSha256 -cne $Server.pdbSha256 -or
        [IO.Path]::GetFileName([string] $binary.AssemblyPath) -cne 'KeyLoad.Server.dll' -or
        [IO.Path]::GetFileName([string] $binary.PdbPath) -cne 'KeyLoad.Server.pdb' -or
        $binary.ExecutableSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $binary.SourceCompositionSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or $binary.SourceCount -le 0) {
        throw 'The native server files differ from the original compiled source manifest.'
    }
}

function Assert-JoinedLifecycle([string] $Directory, [object] $Server) {
    $startup = @(Read-OriginalJson (Join-Path $Directory 'native-process-startup.json') 1048576)
    $before = @(Read-OriginalJson (Join-Path $Directory 'native-process-before-shutdown.json') 1048576)
    $shutdown = Read-OriginalJson (Join-Path $Directory 'native-process-shutdown.json') 1048576
    if ($startup.Count -eq 0 -or $before.Count -ne $startup.Count -or
        $shutdown.Processes.Count -ne $before.Count -or $shutdown.Resources.Count -ne $before.Count -or
        [string]::IsNullOrWhiteSpace($shutdown.JoinedAtUtc)) {
        throw 'Every observed native server requires original startup and joined shutdown evidence.'
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($process in $startup) {
        Assert-ProcessIdentity $process $Server
        if (-not $names.Add([string] $process.Name)) { throw 'A native startup resource is duplicated.' }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($process in $before) {
        Assert-ProcessIdentity $process $Server
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

$root = [IO.Path]::GetFullPath($Repository)
$results = [IO.Path]::GetFullPath($ResultsDirectory)
$relative = [IO.Path]::GetRelativePath($root, $results)
if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
    $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
    throw 'The native lifecycle evidence must belong to this checkout.'
}
$manifest = Read-OriginalJson $SourceManifestPath 16777216
$servers = @($manifest.compiledProducts | Where-Object module -CEQ 'KeyLoad.Server')
if ($servers.Count -ne 1 -or $manifest.repository -cne $root) {
    throw 'Exactly one original native server compilation identity is required.'
}
$originals = @(Get-ChildItem -LiteralPath $results -Filter 'native-process-startup.json' -Recurse -File)
if ($originals.Count -eq 0 -or $originals.Count -gt 4096) { throw 'Native lifecycle originals are missing or exceed their bound.' }
$processes = 0
$files = [Collections.Generic.List[object]]::new()
foreach ($original in $originals) {
    $processes += Assert-JoinedLifecycle $original.DirectoryName $servers[0].compiledIdentity
    foreach ($name in @('native-process-startup.json','native-process-before-shutdown.json','native-process-shutdown.json')) {
        $file = Get-Item -LiteralPath (Join-Path $original.DirectoryName $name)
        $files.Add([ordered]@{ path = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
            length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
    }
}
[ordered]@{ sourceRevision = $manifest.sourceRevision; processCount = $processes; originals = $files.ToArray() } |
    ConvertTo-Json -Depth 8 -Compress
