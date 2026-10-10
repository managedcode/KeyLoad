[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $EvidenceRoot,
    [Parameter(Mandatory)][ValidateSet('KL-008','KL-011','KL-014','KL-015','KL-021','KL-027','KL-036','KL-033','KL-035','KL-029','KL-034','KL-042','KL-079')][string] $Task,
    [Parameter(Mandatory)][ValidateSet('normal','scalar')][string] $Profile,
    [Parameter(Mandatory)][string] $ContractPath,
    [Parameter(Mandatory)][string] $ExecutionManifestPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repository = [IO.Path]::GetFullPath($Repository)
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$toolRoot = Join-Path $Repository 'scripts/Features/CodeQuality'
foreach ($name in @('shared','parse','compiled-identity','compile-identity','test-identity','image-manifests',
        'native-merge.inputs','native-merge.files','native-merge.contributors',
        'native-merge.trx','native-merge.unit-selectors','native-merge.test-images',
        'native-census.rebind')) {
    . (Join-Path $toolRoot ('functional-coverage.' + $name + '.ps1'))
}
$script:FcNativeMergeInput.ReadBufferBytes = 65536
$scope = 'task-scoped; no product/fullsuite/coverage promotion'
$invalid = 'Task acceptance evidence is incomplete, changed or outside its exact native scope.'
$originals = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$bounds = @{ maximumManifestBytes = 33554432; readBufferBytes = 65536 }
Assert-FcNativeNoReparsePath $Repository
Assert-FcNativeNoReparsePath $EvidenceRoot

function Read-AcOriginal([string] $Path, [bool] $AllowEmpty = $false, [bool] $External = $false) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $External) {
        $relative = [IO.Path]::GetRelativePath($EvidenceRoot, $full).Replace('\','/')
        if ((Resolve-FcNativeEvidencePath $EvidenceRoot $relative 4096) -cne $full) { throw $invalid }
    }
    Assert-FcNativeNoReparsePath $full
    $info = Get-Item -LiteralPath $full -Force
    if ($info -isnot [IO.FileInfo] -or $info.Length -gt 33554432 -or
        (-not $AllowEmpty -and $info.Length -eq 0)) { throw $invalid }
    [byte[]] $bytes = [byte[]]::new(0)
    if ($info.Length -gt 0) {
        $bytes = (Read-FcNativeBoundedFile $full 33554432 65536 $invalid).bytes
    }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$bytes)).ToLowerInvariant()
    if ($originals.ContainsKey($full)) {
        if ($originals[$full].sha256 -cne $hash) { throw $invalid }
    } else { $originals.Add($full, [ordered]@{ path = $full; length = $bytes.Length; sha256 = $hash }) }
    ,$bytes
}
function Get-AcOriginalTrx([string] $Directory) {
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($Directory)
    $entries = 0
    while ($pending.Count -gt 0) {
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            $entries++; if ($entries -gt 10000) { throw $invalid }
            $item = Get-Item -LiteralPath $path -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw $invalid }
            if ($item -is [IO.DirectoryInfo]) { $pending.Push($item.FullName) }
            elseif ($item -is [IO.FileInfo] -and $item.Extension -ceq '.trx') { $item.FullName }
        }
    }
}
function Read-AcJson([string] $Path, [bool] $External = $false) {
    $bytes = Read-AcOriginal $Path $false $External
    $document = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($bytes))
    try { Assert-FcNativeJsonUnique $document.RootElement } finally { $document.Dispose() }
    ConvertFrom-Json ([Text.Encoding]::UTF8.GetString($bytes)) -AsHashtable -Depth 32
}
function Assert-AcProcess([string] $Path, [string] $Stdout, [string] $Stderr, [object] $ExitCode) {
    $process = Read-AcJson $Path
    Assert-FcNativeExactKeys $process @('executable','arguments','timeoutSeconds','settlementSeconds','environment','nativeResult')
    $result = $process.nativeResult
    Assert-FcNativeExactKeys $result @('exitCode','stdout','stderr','failures','exitJoined','outputJoined','errorJoined','disposed')
    if (($ExitCode -isnot [int] -and $ExitCode -isnot [long]) -or
        ($result.exitCode -isnot [int] -and $result.exitCode -isnot [long])) { throw $invalid }
    if ($ExitCode -ne 0 -or $result.exitCode -ne $ExitCode -or $result.failures -isnot [array] -or
        $result.failures.Count -ne 0 -or $process.settlementSeconds -ne 30 -or
        $process.arguments -isnot [array]) { throw $invalid }
    foreach ($name in @('exitJoined','outputJoined','errorJoined','disposed')) {
        if ($result[$name] -isnot [bool] -or -not $result[$name]) { throw $invalid }
    }
    foreach ($entry in @(@($Stdout,'stdout'),@($Stderr,'stderr'))) {
        $actual = Read-AcOriginal $entry[0] $true
        $expected = [Text.UTF8Encoding]::new($false).GetBytes([string] $result[$entry[1]])
        if ($actual.Length -ne $expected.Length -or
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$actual)) -cne
            [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$expected))) { throw $invalid }
    }
    Assert-FcNativeExactKeys $process.environment @('DOTNET_EnableHWIntrinsic')
    $intrinsics = if ($Profile -ceq 'scalar') { '0' } else { $null }
    if ($process.environment.DOTNET_EnableHWIntrinsic -cne $intrinsics) { throw $invalid }
    $process
}

$contract = Read-AcJson $ContractPath $true
Assert-FcNativeExactKeys $contract @('schemaVersion','qualifiedScope','maximumParallelTests','tasks')
$manifest = Read-AcJson $ExecutionManifestPath
if ($contract.maximumParallelTests -ne 50 -or $contract.schemaVersion -ne 1 -or $contract.qualifiedScope -cne $scope -or
    $manifest.schemaVersion -ne 1 -or $manifest.task -cne $Task -or $manifest.profile -cne $Profile -or
    $manifest.qualifiedScope -cne $scope -or $manifest.contractPath -cne $ContractPath -or
    $manifest.contractSha256 -cne $originals[[IO.Path]::GetFullPath($ContractPath)].sha256) { throw $invalid }
$declared = @($contract.tasks | Where-Object { $_.taskId -ceq $Task })
if ($declared.Count -ne 1 -or $declared[0].selections -isnot [array] -or
    $declared[0].selections.Count -eq 0 -or $declared[0].selections.Count -gt 32 -or
    $manifest.selections.Count -ne $declared[0].selections.Count) { throw $invalid }
if ($ContractPath -cne (Join-Path $toolRoot 'task-acceptance.contract.json') -or
    $manifest.sourceManifestPath -cne (Join-Path ([IO.Path]::GetDirectoryName($EvidenceRoot)) 'functional-coverage.production-source-manifest.json')) { throw $invalid }
$source = Read-AcJson $manifest.sourceManifestPath $true
if ($source.schemaVersion -ne 3 -or $source.sourceRevision -cne $manifest.sourceRevision -or
    $manifest.sourceRevision -cnotmatch '\A[0-9a-f]{40}\z') { throw $invalid }
$verification = Assert-AcProcess $manifest.sourceVerificationProcessPath `
    $manifest.sourceVerificationStdoutPath $manifest.sourceVerificationStderrPath $manifest.sourceVerificationExitCode
$expectedVerification = @('-NoProfile','-File',(Join-Path $toolRoot 'functional-coverage.production-source-manifest.ps1'),
    '-Mode','verify','-Root',$Repository,'-EvidenceRoot',[IO.Path]::GetDirectoryName($manifest.sourceManifestPath))
if ($verification.executable -cne 'pwsh' -or
    (ConvertTo-Json $verification.arguments -Compress) -cne (ConvertTo-Json $expectedVerification -Compress)) { throw $invalid }
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$union = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$uids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$verified = [Collections.Generic.List[object]]::new()
foreach ($selection in $declared[0].selections) {
    if (-not $ids.Add([string] $selection.selectionId) -or $selection.cases.Count -eq 0 -or
        $selection.cases.Count -gt 5000 -or $selection.suite -cnotin @('unit','recovery','rf3')) { throw $invalid }
    $rows = @($manifest.selections | Where-Object { $_.selectionId -ceq $selection.selectionId })
    if ($rows.Count -ne 1) { throw $invalid }
    $row = $rows[0]
    if ($row.state -cne 'executed' -or $row.suite -cne $selection.suite -or $row.filter -cne $selection.filter) { throw $invalid }
    $before = Read-AcJson $row.imageIdentityManifestPath
    $after = Read-AcJson $row.imageIdentityAfterPath
    if ((ConvertTo-Json $before -Depth 32 -Compress) -cne (ConvertTo-Json $after -Depth 32 -Compress)) { throw $invalid }
    $prepared = @($source.compiledTestsManifest | Where-Object { $_.suite -ceq $selection.suite })
    if ($prepared.Count -ne 1) { throw $invalid }
    $preparedPath = Join-Path ([IO.Path]::GetDirectoryName($manifest.sourceManifestPath)) $prepared[0].fileName
    $preparedImage = Read-AcJson $preparedPath $true
    if ($originals[[IO.Path]::GetFullPath($preparedPath)].sha256 -cne $prepared[0].sha256 -or
        (ConvertTo-Json $preparedImage -Depth 32 -Compress) -cne (ConvertTo-Json $before -Depth 32 -Compress)) { throw $invalid }
    $image = Read-FcNativeTestIdentityManifest $row.imageIdentityManifestPath $Repository $selection.suite $bounds
    if (-not $image.compiledIdentity.compiledSourceBindingComplete -or
        @($image.compiledIdentity.sourceFilesWithoutPdbDocuments).Count -ne 0) { throw $invalid }
    $assembly = [Reflection.AssemblyName]::GetAssemblyName((Resolve-FcPath $Repository $image.compiledIdentity.dll)).FullName
    if ($row.assemblyFullName -cne $assembly) { throw $invalid }
    $discovery = Assert-AcProcess $row.discoveryProcessPath $row.discoveryStdoutPath $row.discoveryStderrPath $row.discoveryExitCode
    $execution = Assert-AcProcess $row.executionProcessPath $row.executionStdoutPath $row.executionStderrPath $row.executionExitCode
    $dll = Resolve-FcPath $Repository $image.compiledIdentity.dll
    $expectedDiscovery = @($dll,'--list-tests','json','--disable-logo','--ansi','off','--progress','off','--output','Detailed',
        '--results-directory',(Join-Path ([IO.Path]::GetDirectoryName($row.discoveryProcessPath)) 'discovery-native'),
        '--treenode-filter',$selection.filter)
    $expectedExecution = @((Join-Path $Repository 'scripts/Features/TestInfrastructure/run-tests.mjs'),
        ('--KeyLoadTests:Suite=' + $selection.suite),('--KeyLoadTests:Filter=' + $selection.filter),
        ('--KeyLoadTests:ResultsDirectory=' + $row.resultsDirectory),'--KeyLoadTests:ReportTrx=true',
        '--KeyLoadTests:Execution:MaximumParallelTests=50')
    $timeout = if ($selection.suite -ceq 'rf3') { 3600 } else { 1800 }
    if ($discovery.executable -cne 'dotnet' -or $discovery.timeoutSeconds -ne 1800 -or
        $execution.executable -cne 'node' -or $execution.timeoutSeconds -ne $timeout -or
        (ConvertTo-Json $discovery.arguments -Compress) -cne (ConvertTo-Json $expectedDiscovery -Compress) -or
        (ConvertTo-Json $execution.arguments -Compress) -cne (ConvertTo-Json $expectedExecution -Compress) -or
        $row.discoveryJsonPath -cne $row.discoveryStdoutPath) { throw $invalid }
    $census = Read-AcJson $row.discoveryJsonPath
    Assert-FcNativeExactKeys $census @('schemaVersion','tests')
    if ($census.schemaVersion -ne 1 -or $census.tests -isnot [array]) { throw $invalid }
    $expected = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($case in $selection.cases) {
        $key = Get-FcNativeTrxKey $case.nativeReportClassName $case.methodName $case.instanceName
        $expected.Add($key, $case)
        if (-not $union.Add($selection.suite + '|' + $key)) { throw $invalid }
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($test in $census.tests) {
        Assert-FcNativeExactKeys $test @('uid','displayName','type','location')
        Assert-FcNativeObservedTestType $test.type $assembly
        $key = Get-FcNativeTrxKey ($test.type.namespace + '.' + $test.type.typeName) $test.type.methodName $test.displayName
        if (-not $expected.ContainsKey($key) -or -not $seen.Add($key) -or
            [string]::IsNullOrWhiteSpace($test.uid) -or -not $uids.Add($selection.suite + '|' + $test.uid)) { throw $invalid }
        $case = $expected[$key]
        if (-not $test.uid.StartsWith($case.className + '.', [StringComparison]::Ordinal) -and
            -not $test.uid.StartsWith($case.className + '(', [StringComparison]::Ordinal)) { throw $invalid }
        Assert-FcNativeExactKeys $test.location @('file','lineStart','lineEnd')
        Assert-FcNativeReboundLocation $test.location $case $Repository
        if (@($image.sources | Where-Object { $_.path -ceq $case.sourcePath }).Count -ne 1) { throw $invalid }
        if ($case.Contains('parameterTypeFullNames') -and
            (ConvertTo-Json $case.parameterTypeFullNames -Compress) -cne
            (ConvertTo-Json $test.type.parameterTypeFullNames -Compress)) { throw $invalid }
    }
    if (-not $seen.SetEquals([string[]] $expected.Keys)) { throw $invalid }
    Assert-FcNativeNoReparsePath $row.resultsDirectory
    $actualTrx = @(Get-AcOriginalTrx $row.resultsDirectory | Sort-Object -CaseSensitive)
    if ($row.originalTrxPaths.Count -ne 1 -or $actualTrx.Count -ne 1 -or
        $actualTrx[0] -cne $row.originalTrxPaths[0]) { throw $invalid }
    [void](Read-AcOriginal $actualTrx[0])
    $trxCases = @($selection.cases | ForEach-Object { @{ className = $_.className; methodName = $_.methodName; instanceName = $_.instanceName } })
    $outcome = Read-FcNativeTrx $actualTrx[0] $selection.suite $trxCases
    $xml = Read-FcXml $actualTrx[0] 'TestRun'
    $summary = $xml.SelectSingleNode('//*[local-name()="ResultSummary"]')
    $counters = $summary.SelectSingleNode('./*[local-name()="Counters"]')
    if ($summary.GetAttribute('outcome') -cnotin @('Completed','Passed') -or
        (Convert-FcCount $counters.GetAttribute('warning')) -ne 0 -or
        (Convert-FcCount $counters.GetAttribute('completed')) -ne 0) { throw $invalid }
    $verified.Add([ordered]@{ selectionId = $selection.selectionId; nativeCases = $census.tests; originalTrxCases = $outcome.cases })
}
# Re-read every captured original before publishing the separate acceptance receipt.
foreach ($path in @($originals.Keys)) { [void](Read-AcOriginal $path $true $true) }
$receipt = [ordered]@{ schemaVersion = 1; task = $Task; profile = $Profile; sourceRevision = $manifest.sourceRevision
    qualifiedScope = $scope; contractSha256 = $manifest.contractSha256; caseCount = $union.Count
    selections = @($verified); originals = @($originals.Values | Sort-Object path) }
$receiptPath = Join-Path $EvidenceRoot 'acceptance-receipt.json'
$bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json $receipt -Depth 32) + "`n")
$stream = [IO.FileStream]::new($receiptPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
Write-Output ('Exact native task acceptance verified: ' + $Task + '/' + $Profile + '; cases=' + $union.Count)
