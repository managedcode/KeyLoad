$script:FcNativeMergeProcess = [ordered]@{
    ToolRelativePath = 'tools/net8.0/any/dotnet-coverage.dll'
    CoverageFormat = 'coverage'
    XmlFormat = 'xml'
    CoberturaFormat = 'cobertura'
    TimeoutMessage = 'The native coverage merge process exceeded its admitted settlement deadline.'
    OutputMessage = 'The native coverage merge process output exceeded its admitted bound.'
    ExitMessage = 'The native coverage merge process failed.'
    TelemetryVariable = 'DOTNET_COVERAGE_TELEMETRY_OPTOUT'
    NoLogoVariable = 'DOTNET_COVERAGE_NOLOGO'
    EnabledValue = '1'
    FailureExitCode = 1
    NotStartedExitCode = -1
    UnsettledOwnerMessage = 'The native coverage process owner could not settle its original process and readers within the admitted bound.'
}

function New-FcCoverageProcessStartInfo([string] $Executable, [string[]] $Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment[$script:FcNativeMergeProcess.TelemetryVariable] = $script:FcNativeMergeProcess.EnabledValue
    $start.Environment[$script:FcNativeMergeProcess.NoLogoVariable] = $script:FcNativeMergeProcess.EnabledValue
    $start
}

function Add-FcProcessChunk([string] $Text, [Text.StringBuilder] $Buffer, [long] $MaximumCharacters,
    [Collections.Generic.List[Exception]] $Failures, [Collections.IDictionary] $Counter) {
    if ($Counter.Length -gt $MaximumCharacters - $Text.Length) {
        $remaining = [Math]::Max(0, $MaximumCharacters - $Counter.Length)
        if ($remaining -gt 0) { [void] $Buffer.Append($Text, 0, [int] $remaining) }
        $Counter.Length = $MaximumCharacters
        if (-not $Counter.Overflow) {
            $Counter.Overflow = $true
            $Failures.Add([InvalidOperationException]::new($script:FcNativeMergeProcess.OutputMessage))
        }
        return
    }
    $Counter.Length += $Text.Length
    [void] $Buffer.Append($Text)
}

function New-FcProcessReader([IO.StreamReader] $Reader, [char[]] $Buffer) {
    [ordered]@{ reader = $Reader; buffer = $Buffer; task = $Reader.ReadAsync($Buffer, 0, $Buffer.Length) }
}

function Start-FcNextProcessRead([Collections.IDictionary] $State) {
    $State.task = $State.reader.ReadAsync($State.buffer, 0, $State.buffer.Length)
}

function Consume-FcProcessRead([Collections.IDictionary] $State, [Text.StringBuilder] $Output,
    [long] $MaximumCharacters, [Collections.Generic.List[Exception]] $Failures, [Collections.IDictionary] $Counter) {
    $count = $State.task.GetAwaiter().GetResult()
    if ($count -eq 0) { return $false }
    if (-not $Counter.Overflow) {
        Add-FcProcessChunk ([string]::new([char[]] $State.buffer, 0, $count)) $Output $MaximumCharacters $Failures $Counter
    }
    if ($Counter.Overflow) { return $false }
    Start-FcNextProcessRead $State
    $true
}

function Stop-FcProcessTree([Diagnostics.Process] $Process, [Collections.Generic.List[Exception]] $Failures) {
    try {
        if (-not $Process.HasExited) { $Process.Kill($true) }
    }
    catch [InvalidOperationException] {
        if (-not $Process.HasExited) { $Failures.Add($_.Exception) }
    }
    catch [System.ComponentModel.Win32Exception] { $Failures.Add($_.Exception) }
}

function Add-FcProcessFailure([Exception] $Failure, [Collections.Generic.List[Exception]] $Failures) {
    $Failures.Add($Failure)
}

function Close-FcProcessReaders([Collections.IDictionary] $State, [Collections.Generic.List[Exception]] $Failures) {
    if ($State.readersClosed) { return }
    $State.readersClosed = $true
    foreach ($reader in @($State.stdoutReader,$State.stderrReader)) {
        try { $reader.Dispose() }
        catch [System.Exception] { Add-FcProcessFailure $_.Exception $Failures }
    }
}

function Start-FcProcessSettlement([Diagnostics.Process] $Process, [Collections.IDictionary] $State,
    [Collections.Generic.List[Exception]] $Failures, [int] $SettlementTimeoutSeconds) {
    if ($State.settling) { return }
    $State.settling = $true
    $State.deadline = [Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds($SettlementTimeoutSeconds))
    Close-FcProcessReaders $State $Failures
    Stop-FcProcessTree $Process $Failures
}

function Stop-FcNativeMergeOwner([Collections.IDictionary] $State) {
    if (-not $State.exitTask.IsCompleted -or -not $State.stdout.task.IsCompleted -or -not $State.stderr.task.IsCompleted -or
        -not $State.exitCodeObserved) {
        [Console]::Error.WriteLine($script:FcNativeMergeProcess.UnsettledOwnerMessage)
        exit $script:FcNativeMergeProcess.FailureExitCode
    }
}

function Get-FcProcessPendingTasks([Collections.IDictionary] $State) {
    $pending = [Collections.Generic.List[Threading.Tasks.Task]]::new()
    if (-not $State.exitJoined) { $pending.Add($State.exitTask) }
    if (-not $State.outputJoined) { $pending.Add($State.stdout.task) }
    if (-not $State.errorJoined) { $pending.Add($State.stderr.task) }
    if ($null -ne $State.deadline) { $pending.Add($State.deadline) }
    $pending.ToArray()
}

function Observe-FcProcessTask([Diagnostics.Process] $Process, [Collections.IDictionary] $State,
    [Threading.Tasks.Task] $Completed, [Text.StringBuilder] $Output, [Text.StringBuilder] $ErrorOutput,
    [long] $MaximumCharacters, [Collections.Generic.List[Exception]] $Failures) {
    if (-not $State.exitJoined -and [object]::ReferenceEquals($Completed,$State.exitTask)) {
        try { $State.exitTask.GetAwaiter().GetResult(); $State.exitCode = $Process.ExitCode; $State.exitCodeObserved = $true }
        catch [System.Exception] {
            Add-FcProcessFailure $_.Exception $Failures
            try { $State.exitCode = $Process.ExitCode; $State.exitCodeObserved = $true }
            catch [InvalidOperationException] { Add-FcProcessFailure $_.Exception $Failures }
            if (-not $State.exitCodeObserved) {
                Start-FcProcessSettlement $Process $State $Failures $State.settlementTimeoutSeconds
            }
        }
        $State.exitJoined = $true
        if ($State.exitCodeObserved -and $State.exitCode -ne 0 -and -not $State.exitFailureRecorded) {
            Add-FcProcessFailure ([InvalidOperationException]::new($script:FcNativeMergeProcess.ExitMessage)) $Failures
            $State.exitFailureRecorded = $true
        }
    }
    if (-not $State.outputJoined -and [object]::ReferenceEquals($Completed,$State.stdout.task)) {
        try { $State.outputJoined = -not (Consume-FcProcessRead $State.stdout $Output $MaximumCharacters $Failures $State.counter) }
        catch [System.Exception] { Add-FcProcessFailure $_.Exception $Failures; $State.outputJoined = $true; Start-FcProcessSettlement $Process $State $Failures $State.settlementTimeoutSeconds }
    }
    if (-not $State.errorJoined -and [object]::ReferenceEquals($Completed,$State.stderr.task)) {
        try { $State.errorJoined = -not (Consume-FcProcessRead $State.stderr $ErrorOutput $MaximumCharacters $Failures $State.counter) }
        catch [System.Exception] { Add-FcProcessFailure $_.Exception $Failures; $State.errorJoined = $true; Start-FcProcessSettlement $Process $State $Failures $State.settlementTimeoutSeconds }
    }
    if ($State.counter.Overflow -and -not $State.settling) { Start-FcProcessSettlement $Process $State $Failures $State.settlementTimeoutSeconds }
}

function StartAndDrain-FcCoverageProcess([Diagnostics.Process] $Process, [int] $TimeoutSeconds,
    [int] $SettlementTimeoutSeconds, [int] $MaximumOutputCharacters, [Collections.Generic.List[Exception]] $Failures) {
    $started = $false
    try { $started = $Process.Start() }
    catch [System.Exception] { Add-FcProcessFailure $_.Exception $Failures }
    if (-not $started) { return [ordered]@{ started = $false; exitCode = $script:FcNativeMergeProcess.NotStartedExitCode; stdout = ''; stderr = ''; exitJoined = $true; outputJoined = $true; errorJoined = $true; disposed = $false } }
    $output = [Text.StringBuilder]::new(); $errorOutput = [Text.StringBuilder]::new()
    $stdoutReader = $Process.StandardOutput; $stderrReader = $Process.StandardError
    $state = [ordered]@{ exitTask = $Process.WaitForExitAsync(); stdoutReader = $stdoutReader; stderrReader = $stderrReader
        stdout = (New-FcProcessReader $stdoutReader ([char[]]::new($script:FcNativeMergeInput.ReadBufferBytes)))
        stderr = (New-FcProcessReader $stderrReader ([char[]]::new($script:FcNativeMergeInput.ReadBufferBytes)))
        exitJoined = $false; outputJoined = $false; errorJoined = $false; exitCode = $script:FcNativeMergeProcess.NotStartedExitCode
        exitCodeObserved = $false; exitFailureRecorded = $false; settlementFailureRecorded = $false; settling = $false; readersClosed = $false
        deadline = [Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds($TimeoutSeconds))
        settlementTimeoutSeconds = $SettlementTimeoutSeconds
        counter = [ordered]@{ Length = 0L; Overflow = $false } }
    while (-not $state.exitJoined -or -not $state.outputJoined -or -not $state.errorJoined -or -not $state.exitCodeObserved) {
        $completed = [Threading.Tasks.Task]::WhenAny((Get-FcProcessPendingTasks $state)).GetAwaiter().GetResult()
        if ([object]::ReferenceEquals($completed,$state.deadline)) {
            if (-not $state.settling) {
                Add-FcProcessFailure ([TimeoutException]::new($script:FcNativeMergeProcess.TimeoutMessage)) $Failures
                Start-FcProcessSettlement $Process $state $Failures $SettlementTimeoutSeconds
                continue
            }
            if ($state.exitTask.IsCompleted -and $state.stdout.task.IsCompleted -and $state.stderr.task.IsCompleted) {
                foreach ($original in @($state.exitTask,$state.stdout.task,$state.stderr.task)) {
                    Observe-FcProcessTask $Process $state $original $output $errorOutput $MaximumOutputCharacters $Failures
                }
                if ($state.exitCodeObserved) {
                    $state.deadline = $null
                    continue
                }
            }
            if (-not $state.settlementFailureRecorded) {
                Add-FcProcessFailure ([TimeoutException]::new($script:FcNativeMergeProcess.TimeoutMessage)) $Failures
                $state.settlementFailureRecorded = $true
            }
            Stop-FcNativeMergeOwner $state
        }
        Observe-FcProcessTask $Process $state $completed $output $errorOutput $MaximumOutputCharacters $Failures
    }
    [ordered]@{ started = $true; exitCode = $state.exitCode; stdout = $output.ToString(); stderr = $errorOutput.ToString()
        exitJoined = $true; outputJoined = $true; errorJoined = $true; disposed = $false }
}

function Invoke-FcCoverageProcess([string] $Executable, [string[]] $Arguments, [int] $TimeoutSeconds,
    [int] $MaximumOutputCharacters) {
    if ($TimeoutSeconds -le 0 -or $MaximumOutputCharacters -le 0 -or $script:FcNativeMergeInput.SettlementTimeoutSeconds -le 0) {
        throw $script:FcNativeMergeProcess.ExitMessage
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = New-FcCoverageProcessStartInfo $Executable $Arguments
    $failures = [Collections.Generic.List[Exception]]::new()
    $state = StartAndDrain-FcCoverageProcess $process $TimeoutSeconds $script:FcNativeMergeInput.SettlementTimeoutSeconds $MaximumOutputCharacters $failures
    $disposed = $false
    try { $process.Dispose(); $disposed = $true }
    catch [System.Exception] { $failures.Add($_.Exception) }
    if (-not $state.exitJoined -or -not $state.outputJoined -or -not $state.errorJoined -or -not $disposed) {
        $failures.Add([TimeoutException]::new($script:FcNativeMergeProcess.TimeoutMessage))
    }
    [ordered]@{ exitCode = $state.exitCode; stdout = $state.stdout; stderr = $state.stderr
        failures = @($failures); exitJoined = $state.exitJoined; outputJoined = $state.outputJoined
        errorJoined = $state.errorJoined; disposed = $disposed }
}

function Invoke-FcCoverageMerge([string] $ToolRoot, [string[]] $Inputs, [string] $Output,
    [string] $LogPath, [string] $Format, [int] $TimeoutSeconds, [int] $MaximumOutputCharacters, [long] $MaximumOutputBytes) {
    if ([IO.File]::Exists($Output) -or [IO.Directory]::Exists($Output)) { throw 'A native coverage output path already exists.' }
    $dll = Join-Path $ToolRoot $script:FcNativeMergeProcess.ToolRelativePath
    if (-not [IO.File]::Exists($dll) -or ((Get-Item -LiteralPath $dll -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw $script:FcNativeMergeProcess.ExitMessage }
    $arguments = [Collections.Generic.List[string]]::new()
    $arguments.Add($dll); $arguments.Add('merge')
    foreach ($inputPath in $Inputs) { $arguments.Add($inputPath) }
    $arguments.Add('--output'); $arguments.Add($Output)
    $arguments.Add('--output-format'); $arguments.Add($Format)
    $arguments.Add('--disable-console-output')
    $arguments.Add('--nologo')
    $result = Invoke-FcCoverageProcess 'dotnet' $arguments.ToArray() $TimeoutSeconds $MaximumOutputCharacters
    $log = [Text.UTF8Encoding]::new($false).GetBytes(($result.stdout + $result.stderr))
    $logFailure = $null
    try {
        if ($log.LongLength -gt $MaximumOutputBytes) { throw $script:FcNativeMergeProcess.OutputMessage }
        Write-FcNativeCreateOnly $LogPath $log
    }
    catch [System.Exception] { $logFailure = $_.Exception }
    if ($null -ne $logFailure -and ($result.failures.Count -gt 0 -or $result.exitCode -ne 0)) {
        $failures = [Collections.Generic.List[Exception]]::new()
        foreach ($failure in $result.failures) { $failures.Add($failure) }
        if ($result.exitCode -ne 0) { $failures.Add([InvalidOperationException]::new($script:FcNativeMergeProcess.ExitMessage)) }
        $failures.Add($logFailure)
        throw [AggregateException]::new($script:FcNativeMergeProcess.ExitMessage, $failures.ToArray())
    }
    if ($null -ne $logFailure) { throw $logFailure }
    if ($result.failures.Count -gt 0) { throw [AggregateException]::new($script:FcNativeMergeProcess.ExitMessage, [Exception[]] $result.failures) }
    if ($result.exitCode -ne 0) { throw $script:FcNativeMergeProcess.ExitMessage }
    if (-not [IO.File]::Exists($Output)) { throw $script:FcNativeMergeProcess.ExitMessage }
    $info = Get-Item -LiteralPath $Output -Force
    if ($info -isnot [IO.FileInfo] -or $info.Length -le 0 -or $info.Length -gt $MaximumOutputBytes -or
        ($info.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw $script:FcNativeMergeProcess.ExitMessage }
    $result
}

function Read-FcNativeToolClosure([string] $ToolRoot, [string] $Version, [object] $Bounds, [object[]] $ContextEntries) {
    $helper = Join-Path $PSScriptRoot 'functional-coverage.native-merge.tool.mjs'
    $boundsJson = ConvertTo-Json -InputObject $Bounds -Depth 4 -Compress
    $entriesJson = if ($null -eq $ContextEntries) { 'null' } else { ConvertTo-Json -InputObject @($ContextEntries) -Depth 4 -Compress }
    $arguments = [string[]] @($helper,$ToolRoot,$Version,$boundsJson,$entriesJson)
    $result = Invoke-FcCoverageProcess 'node' $arguments ([int] $Bounds.applicationCleanupTimeoutSeconds) `
        ([int] $Bounds.maximumManifestBytes)
    if ($result.failures.Count -gt 0 -or $result.exitCode -ne 0) { throw $script:FcNativeMergeProcess.ExitMessage }
    try { $closure = ConvertFrom-Json -InputObject $result.stdout -AsHashtable -Depth 4 }
    catch [System.Exception] { throw $script:FcNativeMergeProcess.ExitMessage }
    if ($closure.version -cne $Version -or $closure.nupkgSha256 -cnotmatch '\A[0-9a-f]{64}\z' -or
        $closure.nupkgSha512 -cnotmatch '\A[A-Za-z0-9+/]{86}==$' -or
        $closure.closureDigest -cnotmatch '\A[0-9a-f]{64}\z' -or $closure.packageFileCount -le 0 -or
        $closure.licenseFileCount -le 0) { throw $script:FcNativeMergeProcess.ExitMessage }
    $closure
}
