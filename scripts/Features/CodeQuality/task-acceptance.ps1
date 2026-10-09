[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $EvidenceRoot,
    [Parameter(Mandatory)][ValidateSet('KL-008','KL-011','KL-014','KL-015','KL-021','KL-027','KL-036','KL-033','KL-035','KL-029')][string] $Task,
    [Parameter(Mandatory)][ValidateSet('normal','scalar')][string] $Profile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repository = [IO.Path]::GetFullPath($Repository)
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$toolRoot = Join-Path $Repository 'scripts/Features/CodeQuality'
foreach ($name in @('shared','compiled-identity','compile-identity','inventory','test-identity',
        'production-snapshot','production-evidence','native-merge.inputs','native-merge.files',
        'native-merge.process','native-merge.contributors','product-contributors')) {
    . (Join-Path $toolRoot ('functional-coverage.' + $name + '.ps1'))
}
$script:FcNativeMergeInput.ReadBufferBytes = 65536
$script:FcNativeMergeInput['SettlementTimeoutSeconds'] = 30
# Import the existing native source/PDB/image owner without invoking its prepare/verify entry.
$producer = Join-Path $toolRoot 'functional-coverage.production-source-manifest.ps1'
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($producer, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Canonical source identity producer cannot be parsed.' }
$config = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left.Extent.Text -ceq '$script:Psm' }, $true))
if ($config.Count -ne 1) { throw 'Canonical source identity configuration is ambiguous.' }
. ([scriptblock]::Create($config[0].Extent.Text))
foreach ($function in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($function.Extent.Text))
}
$Repository = Resolve-PsmRoot $Repository
$relative = [IO.Path]::GetRelativePath($Repository, $EvidenceRoot).Replace('\','/')
if ((Resolve-FcPath $Repository $relative) -cne $EvidenceRoot) { throw 'Evidence root must be confined and existing.' }
Assert-FcNativeNoReparsePath $EvidenceRoot

function Write-TaskOriginal([string] $Path, [object] $Value, [bool] $Json = $true) {
    $text = if ($Json) { ConvertTo-Json -InputObject $Value -Depth 32 } else { [string] $Value }
    Write-PsmCreateOnly $Path ([Text.UTF8Encoding]::new($false).GetBytes($text))
}
function Read-TaskJson([string] $Path) {
    $read = Read-FcNativeBoundedFile $Path 33554432 65536 'Invalid bounded task JSON.'
    $document = [Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($read.bytes))
    try { Assert-FcNativeJsonUnique $document.RootElement } finally { $document.Dispose() }
    ConvertFrom-Json ([Text.Encoding]::UTF8.GetString($read.bytes)) -AsHashtable -Depth 64
}
function Invoke-TaskChild([string] $Directory, [string] $Name, [string] $Executable,
    [string[]] $Arguments, [int] $Timeout) {
    $result = Invoke-FcCoverageProcess $Executable $Arguments $Timeout 33554432 -ForwardToConsole $true
    Write-TaskOriginal (Join-Path $Directory ($Name + '.stdout.txt')) $result.stdout $false
    Write-TaskOriginal (Join-Path $Directory ($Name + '.stderr.txt')) $result.stderr $false
    Write-TaskOriginal (Join-Path $Directory ($Name + '.process.json')) ([ordered]@{
        executable = $Executable; arguments = $Arguments; timeoutSeconds = $Timeout
        environment = [ordered]@{ DOTNET_EnableHWIntrinsic = [Environment]::GetEnvironmentVariable('DOTNET_EnableHWIntrinsic') }
        settlementSeconds = 30; nativeResult = $result })
    $result
}
function Assert-TaskSettled([object] $Result) {
    if ($Result.failures.Count -ne 0 -or $Result.exitCode -ne 0 -or -not $Result.exitJoined -or
        -not $Result.outputJoined -or -not $Result.errorJoined -or -not $Result.disposed) {
        throw 'Original native child failed or did not settle; originals retained.'
    }
}
function Assert-TaskDiscovery([object] $Selection, [object] $Image, [string] $Assembly, [string] $Path) {
    $native = Read-TaskJson $Path
    if ($native -isnot [Collections.IDictionary] -or -not $native.Contains('tests')) {
        throw 'Original native discovery has no tests array.'
    }
    $expected = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($case in $Selection.cases) {
        $key = $case.nativeReportClassName + '|' + $case.methodName + '|' + $case.instanceName
        if ($expected.ContainsKey($key)) { throw 'Duplicate declared native task identity.' }
        $expected.Add($key, $case)
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $uids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $native.tests) {
        $reported = $case.type.namespace + '.' + $case.type.typeName
        $key = $reported + '|' + $case.type.methodName + '|' + $case.displayName
        if (-not $expected.ContainsKey($key) -or -not $seen.Add($key) -or -not $uids.Add([string] $case.uid)) {
            throw 'Native task discovery is missing, duplicate or outside its exact declared case union.'
        }
        $declared = $expected[$key]
        $uidScope = '\A' + [regex]::Escape($declared.className) + '(?:\([^\r\n)]{1,1024}\))?\.'
        if ($case.type.assemblyFullName -cne $Assembly -or
            ([string] $case.uid) -cnotmatch $uidScope) {
            throw 'Native UID or assembly does not bind the canonical declared type.'
        }
        $source = [string] $declared.sourcePath
        $location = ([string] $case.location.file).Replace('\','/')
        $absolute = (Resolve-FcPath $Repository $source).Replace('\','/')
        if ($location -cne $absolute -and $location -cne ('/_/' + $source)) {
            throw 'Native discovery source location does not bind the declared source.'
        }
        if ($case.location.lineStart -lt 1 -or $case.location.lineEnd -lt $case.location.lineStart -or
            @($Image.sources | Where-Object { $_.path -ceq $source }).Count -ne 1) {
            throw 'Native discovery source range or native compiled source binding is invalid.'
        }
        if ($declared.Contains('parameterTypeFullNames') -and
            (ConvertTo-Json @($declared.parameterTypeFullNames) -Compress) -cne
            (ConvertTo-Json @($case.type.parameterTypeFullNames) -Compress)) {
            throw 'Native discovery changed its declared parameter types.'
        }
    }
    if ($seen.Count -ne $expected.Count -or $seen.Count -eq 0) { throw 'Native task discovery is incomplete.' }
}
function Invoke-TaskSelection([object] $Selection, [string] $TaskRoot) {
    if ($Selection.selectionId -cnotmatch '\A[a-z][a-z0-9-]{0,79}\z' -or
        $Selection.suite -cnotin @('unit','recovery','rf3')) { throw 'Invalid task selection.' }
    $directory = Join-Path $TaskRoot $Selection.selectionId
    if ([IO.Directory]::Exists($directory)) { throw 'Task selection originals must be fresh.' }
    [void] [IO.Directory]::CreateDirectory($directory)
    $project = switch ($Selection.suite) { unit { 'KeyLoad.UnitTests' } recovery { 'KeyLoad.RecoveryTests' } rf3 { 'KeyLoad.IntegrationTests' } }
    $dll = Resolve-FcPath $Repository ('tests/' + $project + '/bin/Release/net10.0/' + $project + '.dll')
    $image = Get-PsmTestImage $Repository $project
    $preparedReferences = @($source.compiledTestsManifest | Where-Object { $_.suite -ceq $Selection.suite })
    if ($preparedReferences.Count -ne 1) { throw 'Prepared native image reference is absent or ambiguous.' }
    $preparedPath = Resolve-FcNativeEvidencePath $EvidenceRoot ([string] $preparedReferences[0].fileName) 4096
    if ((Get-FcHash $preparedPath) -cne $preparedReferences[0].sha256 -or
        (ConvertTo-Json (Read-TaskJson $preparedPath) -Depth 32 -Compress) -cne
        (ConvertTo-Json $image -Depth 32 -Compress)) {
        throw 'Current native image differs from the original prepared workflow source image.'
    }
    $beforePath = Join-Path $directory 'native-image-before.json'
    Write-TaskOriginal $beforePath $image
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($dll).FullName
    $row = [ordered]@{ selectionId = $Selection.selectionId; suite = $Selection.suite; filter = $Selection.filter
        assemblyFullName = $assembly; imageIdentityManifestPath = $beforePath
        imageIdentityAfterPath = Join-Path $directory 'native-image-after.json'
        discoveryJsonPath = Join-Path $directory 'discovery.stdout.txt'
        discoveryStdoutPath = Join-Path $directory 'discovery.stdout.txt'
        discoveryStderrPath = Join-Path $directory 'discovery.stderr.txt'
        discoveryProcessPath = Join-Path $directory 'discovery.process.json'; discoveryExitCode = $null
        executionStdoutPath = Join-Path $directory 'execution.stdout.txt'
        executionStderrPath = Join-Path $directory 'execution.stderr.txt'
        executionProcessPath = Join-Path $directory 'execution.process.json'; executionExitCode = $null
        resultsDirectory = Join-Path $directory 'results'; originalTrxPaths = @(); state = 'not-started'; failure = $null }
    try {
        $discovery = Invoke-TaskChild $directory 'discovery' 'dotnet' ([string[]] @($dll,
            '--list-tests','json','--disable-logo','--ansi','off','--progress','off','--output','Detailed',
            '--results-directory',(Join-Path $directory 'discovery-native'),'--treenode-filter',$Selection.filter)) 1800
        $row.discoveryExitCode = $discovery.exitCode
        Assert-TaskSettled $discovery
        Assert-TaskDiscovery $Selection $image $assembly $row.discoveryJsonPath
        $row.state = 'discovered'
        $timeout = if ($Selection.suite -ceq 'rf3') { 3600 } else { 1800 }
        $arguments = [string[]] @((Join-Path $Repository 'scripts/Features/TestInfrastructure/run-tests.mjs'),
            ('--KeyLoadTests:Suite=' + $Selection.suite), ('--KeyLoadTests:Filter=' + $Selection.filter),
            ('--KeyLoadTests:ResultsDirectory=' + $row.resultsDirectory), '--KeyLoadTests:ReportTrx=true',
            '--KeyLoadTests:Execution:MaximumParallelTests=20')
        $execution = Invoke-TaskChild $directory 'execution' 'node' $arguments $timeout
        $row.executionExitCode = $execution.exitCode
        if ([IO.Directory]::Exists($row.resultsDirectory)) {
            Assert-FcNativeNoReparsePath $row.resultsDirectory
            $row.originalTrxPaths = @([IO.Directory]::EnumerateFiles($row.resultsDirectory, '*.trx',
                [IO.SearchOption]::AllDirectories) | Sort-Object -CaseSensitive)
        }
        Assert-TaskSettled $execution
        $row.state = 'executed'
    }
    catch [System.Exception] { $row.state = 'failed'; $row.failure = $_.Exception.ToString() }
    $after = Get-PsmTestImage $Repository $project
    Write-TaskOriginal $row.imageIdentityAfterPath $after
    if ((ConvertTo-Json $image -Depth 32 -Compress) -cne (ConvertTo-Json $after -Depth 32 -Compress)) {
        throw 'Native source/image drift; no subsequent operation may start.'
    }
    $row
}

$contractPath = Resolve-FcPath $Repository 'scripts/Features/CodeQuality/task-acceptance.contract.json'
$contract = Read-TaskJson $contractPath
if ($contract.schemaVersion -ne 1 -or $contract.maximumParallelTests -ne 20) { throw 'Unsupported task contract schema.' }
$taskContract = @($contract.tasks | Where-Object { $_.taskId -ceq $Task })
if ($taskContract.Count -ne 1 -or $taskContract[0].selections.Count -eq 0) { throw 'Task contract is absent or ambiguous.' }
$sourceManifestPath = Resolve-FcPath $Repository ($relative + '/functional-coverage.production-source-manifest.json')
$source = Read-TaskJson $sourceManifestPath
$taskRoot = Join-Path $EvidenceRoot ($Task.ToLowerInvariant() + '-' + $Profile)
if ([IO.Directory]::Exists($taskRoot)) { throw 'Task evidence must be fresh.' }
[void] [IO.Directory]::CreateDirectory($taskRoot)
$manifest = [ordered]@{ schemaVersion = 1; task = $Task; profile = $Profile
    sourceRevision = $source.sourceRevision; sourceManifestPath = $sourceManifestPath
    contractPath = $contractPath; contractSha256 = Get-FcHash $contractPath
    qualifiedScope = 'task-scoped; no product/fullsuite/coverage promotion'
    sourceVerificationStdoutPath = Join-Path $taskRoot 'source-verify.stdout.txt'
    sourceVerificationStderrPath = Join-Path $taskRoot 'source-verify.stderr.txt'
    sourceVerificationExitCode = $null; sourceVerificationProcessPath = Join-Path $taskRoot 'source-verify.process.json'
    selections = @() }
$inherited = [Environment]::GetEnvironmentVariable('DOTNET_EnableHWIntrinsic')
$location = Get-Location
try {
    Set-Location -LiteralPath $Repository
    if ($Profile -ceq 'scalar') { [Environment]::SetEnvironmentVariable('DOTNET_EnableHWIntrinsic', '0') }
    else { [Environment]::SetEnvironmentVariable('DOTNET_EnableHWIntrinsic', [NullString]::Value) }
    foreach ($selection in $taskContract[0].selections) {
        $manifest.selections += Invoke-TaskSelection $selection $taskRoot
    }
    $verification = Invoke-TaskChild $taskRoot 'source-verify' 'pwsh' ([string[]] @('-NoProfile','-File',
        $producer,'-Mode','verify','-Root',$Repository,'-EvidenceRoot',$EvidenceRoot)) 1800
    $manifest.sourceVerificationExitCode = $verification.exitCode
    $manifestPath = Join-Path $taskRoot 'execution-manifest.json'
    Write-TaskOriginal $manifestPath $manifest
    # The independent owner verifies original native discovery, exact TRX outcomes and source/image binding.
    & (Join-Path $toolRoot 'task-acceptance.verify.ps1') -Repository $Repository -EvidenceRoot $taskRoot `
        -Task $Task -Profile $Profile -ContractPath $contractPath -ExecutionManifestPath $manifestPath
    Assert-TaskSettled $verification
    if (@($manifest.selections | Where-Object { $_.state -cne 'executed' }).Count -ne 0) {
        throw 'At least one task selection failed; all original outcomes retained.'
    }
}
finally {
    if ($null -eq $inherited) { [Environment]::SetEnvironmentVariable('DOTNET_EnableHWIntrinsic', [NullString]::Value) }
    else { [Environment]::SetEnvironmentVariable('DOTNET_EnableHWIntrinsic', $inherited) }
    Set-Location -LiteralPath $location.Path
}
