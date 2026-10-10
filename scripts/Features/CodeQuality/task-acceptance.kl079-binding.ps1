[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Repository,
    [Parameter(Mandatory)][string] $NormalEvidenceRoot,
    [Parameter(Mandatory)][string] $ScalarEvidenceRoot,
    [Parameter(Mandatory)][string] $SourceRevision,
    [Parameter(Mandatory)][string] $OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repository = [IO.Path]::GetFullPath($Repository)
$toolRoot = Join-Path $Repository 'scripts/Features/CodeQuality'
# Import the canonical owners without executing their prepare/test/verify entry points.
foreach ($name in @('shared','compiled-identity','compile-identity','inventory','test-identity',
        'production-snapshot','production-evidence','native-merge.inputs','native-merge.files',
        'native-merge.process','native-merge.contributors','product-contributors',
        'native-merge.unit-selectors')) {
    . (Join-Path $toolRoot ('functional-coverage.' + $name + '.ps1'))
}
$script:FcNativeMergeInput.ReadBufferBytes = 65536
foreach ($owner in @('functional-coverage.production-source-manifest.ps1','task-acceptance.ps1')) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $toolRoot $owner), [ref] $tokens, [ref] $errors)
    if ($errors.Count -ne 0) { throw 'Canonical binding owner cannot be parsed.' }
    foreach ($assignment in $ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -ceq '$script:Psm'
        }, $true)) { . ([scriptblock]::Create($assignment.Extent.Text)) }
    foreach ($function in $ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst]
        }, $false)) { . ([scriptblock]::Create($function.Extent.Text)) }
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$relativeOutput = [IO.Path]::GetRelativePath($Repository, $OutputDirectory)
if ($SourceRevision -cnotmatch '\A[0-9a-f]{40}\z' -or -not [IO.Directory]::Exists($OutputDirectory) -or
    (-not [IO.Path]::IsPathRooted($relativeOutput) -and $relativeOutput -ne '..' -and
        -not $relativeOutput.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal))) {
    throw 'Binding proposals require an existing private directory and exact source revision.'
}
Assert-FcNativeNoReparsePath $OutputDirectory
$contractPath = Join-Path $toolRoot 'task-acceptance.contract.json'
$contract = Read-TaskJson $contractPath
$declared = @($contract.tasks | Where-Object { $_.taskId -ceq 'KL-079' })
if ($declared.Count -ne 1 -or -not $declared[0].Contains('censusSelections') -or
    $declared[0].selections.Count -ne 0) { throw 'KL079 must remain census-only until reviewed binding.' }
$profileCases = @{}; $profileImages = @{}; $observations = @()
foreach ($profile in @('normal','scalar')) {
    $parent = [IO.Path]::GetFullPath($(if ($profile -ceq 'normal') { $NormalEvidenceRoot } else { $ScalarEvidenceRoot }))
    Assert-FcNativeNoReparsePath $parent
    $root = Join-Path $parent ('kl-079-' + $profile)
    $manifest = Read-TaskJson (Join-Path $root 'execution-manifest.json')
    $source = Read-TaskJson (Join-Path $parent 'functional-coverage.production-source-manifest.json')
    if ($manifest.task -cne 'KL-079' -or $manifest.profile -cne $profile -or
        $manifest.sourceRevision -cne $SourceRevision -or $source.sourceRevision -cne $SourceRevision -or
        $manifest.contractSha256 -cne (Get-FcHash $contractPath) -or $manifest.sourceVerificationExitCode -ne 0 -or
        $manifest.selections.Count -ne $declared[0].censusSelections.Count) { throw 'Census provenance differs.' }
    Assert-TaskSettled (Read-TaskJson (Join-Path $root 'source-verify.process.json')).nativeResult
    $cases = @(); $images = @(); $uids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($selection in $declared[0].censusSelections) {
        $rows = @($manifest.selections | Where-Object { $_.selectionId -ceq $selection.selectionId })
        if ($rows.Count -ne 1 -or $rows[0].state -cne 'census-only-native-admission-pending' -or
            $rows[0].suite -cne $selection.suite -or $rows[0].filter -cne $selection.filter -or
            $rows[0].discoveryExitCode -ne 0 -or $null -ne $rows[0].executionExitCode) { throw 'Census selection failed.' }
        $directory = Join-Path $root $selection.selectionId
        Assert-TaskSettled (Read-TaskJson (Join-Path $directory 'discovery.process.json')).nativeResult
        $image = Read-TaskJson (Join-Path $directory 'native-image-before.json')
        $after = Read-TaskJson (Join-Path $directory 'native-image-after.json')
        $prepared = @($source.compiledTestsManifest | Where-Object { $_.suite -ceq $selection.suite })
        if ($prepared.Count -ne 1) { throw 'Prepared suite image is ambiguous.' }
        $preparedPath = Resolve-FcNativeEvidencePath $parent ([string] $prepared[0].fileName) 4096
        if ((Get-FcHash $preparedPath) -cne $prepared[0].sha256 -or
            (ConvertTo-Json (Read-TaskJson $preparedPath) -Depth 32 -Compress) -cne
            (ConvertTo-Json $image -Depth 32 -Compress) -or
            $image.compiledIdentity.compiledSourceBindingComplete -ne $true) { throw 'Prepared image differs.' }
        if ((ConvertTo-Json $image -Depth 32 -Compress) -cne (ConvertTo-Json $after -Depth 32 -Compress)) {
            throw 'Census image changed.'
        }
        $discoveryPath = Join-Path $directory 'discovery.stdout.txt'
        Assert-TaskCandidateDiscovery $selection $image $rows[0].assemblyFullName $discoveryPath
        $native = Read-TaskJson $discoveryPath
        $bound = @(); $caseKeys = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($test in $native.tests) {
            if (-not $uids.Add([string] $test.uid)) { throw 'Duplicate native UID across selections.' }
            $reported = $test.type.namespace + '.' + $test.type.typeName
            $candidate = @($selection.candidates | Where-Object {
                $reported -cmatch ('\A' + [regex]::Escape($_.className) + '(?:\([^\r\n)]{1,1024}\))?\z') -and
                $test.type.methodName -ceq $_.methodName
            })
            if ($candidate.Count -ne 1) { throw 'Native candidate is ambiguous.' }
            $case = [ordered]@{ className = $candidate[0].className; nativeReportClassName = $reported
                methodName = $test.type.methodName; instanceName = $test.displayName
                sourcePath = $candidate[0].sourcePath; parameterTypeFullNames = @($test.type.parameterTypeFullNames) }
            if ((Get-FcHash (Resolve-FcPath $Repository $case.sourcePath)) -cne
                @($image.sources | Where-Object { $_.path -ceq $case.sourcePath })[0].sha256) {
                throw 'Current source differs from original native case image.'
            }
            $key = $case.nativeReportClassName + '|' + $case.methodName + '|' + $case.instanceName
            if ($caseKeys.ContainsKey($key)) { throw 'Duplicate exact native case identity.' }
            $caseKeys.Add($key, $case)
            $observations += [ordered]@{ profile = $profile; suite = $selection.suite; case = $case
                nativeUid = $test.uid; location = $test.location; source = @($image.sources | Where-Object {
                    $_.path -ceq $case.sourcePath })[0] }
        }
        $orderedKeys = [string[]] @($caseKeys.Keys)
        [Array]::Sort($orderedKeys, [StringComparer]::Ordinal)
        $bound = @($orderedKeys | ForEach-Object { $caseKeys[$_] })
        $cases += [ordered]@{ selectionId = $selection.selectionId; suite = $selection.suite
            filter = $selection.filter; cases = $bound }
        $images += [ordered]@{ selectionId = $selection.selectionId; sources = $image.sources
            buildInputs = $image.buildInputs; dllSha256 = $image.compiledIdentity.dllSha256
            pdbSha256 = $image.compiledIdentity.pdbSha256; mvid = $image.compiledIdentity.mvid
            pdbGuid = $image.compiledIdentity.pdbGuid; pdbStamp = $image.compiledIdentity.pdbStamp }
    }
    $profileCases[$profile] = $cases; $profileImages[$profile] = $images
}
if ((ConvertTo-Json $profileCases.normal -Depth 32 -Compress) -cne
    (ConvertTo-Json $profileCases.scalar -Depth 32 -Compress) -or
    (ConvertTo-Json $profileImages.normal -Depth 32 -Compress) -cne
    (ConvertTo-Json $profileImages.scalar -Depth 32 -Compress)) { throw 'Normal/scalar native binding differs.' }
$strict = [ordered]@{ taskId = 'KL-079'; selections = $profileCases.normal }
Write-TaskOriginal (Join-Path $OutputDirectory 'kl079.strict-task.proposal.json') $strict
Write-TaskOriginal (Join-Path $OutputDirectory 'kl079.native-binding.observations.json') $observations
# The reviewed class/criterion map is source traceability; it cannot claim collected coverage.
$coverage = @(); $contributors = @(); $missingUnit = @()
$inventory = Read-TaskJson (Join-Path $toolRoot 'functional-coverage.unit-test-inventory.json')
foreach ($row in @($observations | Where-Object { $_.profile -ceq 'normal' })) {
    $leaf = $row.case.className.Split('.')[-1]
    $criteria = switch ($leaf) {
        'ChangeFeedTests' { @('001','002','004') }
        'ChangeFeedReadTests' { @('002','005') }
        'ChangeFeedObservedWorkTests' { @('002') }
        'NativeChangeFeedCursorTests' { @('002') }
        'ProjectionProgressTests' { @('001','004','005') }
        'ProjectionReadWorkTests' { @('006') }
        'OutboxPurgeTests' { @('002','004','005') }
        'LiveQueryTests' { @('002','003') }
        'LiveQueryResultCompositionTests' { @('002','003') }
        'ProjectionProcessRecoveryTests' { @('001','004','005') }
        'FeedLiveRf3Tests' { @('002','003','005') }
        'EmptyReplicaSnapshotRf3Tests' { @('002','005') }
        'ClusterTests' { @('001','004','005') }
        default { throw 'Unreviewed coverage class.' }
    }
    $requirements = @($criteria | ForEach-Object { 'REQ-FEED-' + $_ })
    $acceptance = @($criteria | ForEach-Object { 'AC-FEED-' + $_ })
    $modules = @('KeyLoad.Core')
    if ($leaf -cin @('LiveQueryTests','LiveQueryResultCompositionTests','FeedLiveRf3Tests')) {
        $modules += 'KeyLoad.Query'
    }
    $contributors += [ordered]@{ suite = $row.suite; className = $row.case.className
        methodName = $row.case.methodName; instanceName = $row.case.instanceName
        requirements = $requirements; acceptance = $acceptance; executedModules = $modules
        operationOutcomeAndState = 'Original ' + $row.case.methodName +
            ' whole-operation assertions under the current ChangeFeeds criterion map; native covered execution required.' }
    $coverage += [ordered]@{ suite = $row.suite; case = $row.case; location = $row.location; source = $row.source
        requirements = $requirements; acceptance = $acceptance; executedModules = $modules
        status = 'native-bound proposal; actual covered execution and unchanged final verification required' }
    if ($row.suite -ceq 'unit') {
        $found = @($inventory.functionalCases | Where-Object {
            $_.className -ceq $row.case.className -and $_.methodName -ceq $row.case.methodName -and
            $_.instanceName -ceq $row.case.instanceName
        })
        if ($found.Count -gt 1) { throw 'Duplicate old functional case.' }
        if ($found.Count -eq 0) {
            if ($leaf -cne 'ChangeFeedObservedWorkTests') { throw 'Unexpected missing functional case.' }
            $missingUnit += [ordered]@{ className = $row.case.className; nativeReportClassName = $row.case.nativeReportClassName
                methodName = $row.case.methodName; instanceName = $row.case.instanceName
                sourcePath = $row.case.sourcePath; sourceSha256 = $row.source.sha256
                lineNumber = $row.location.lineStart; endLineNumber = $row.location.lineEnd
                classification = 'functional'; exclusionReason = $null; requirements = $requirements; acceptance = $acceptance }
        }
    }
}
Write-TaskOriginal (Join-Path $OutputDirectory 'kl079.coverage-binding.proposal.json') $coverage
Write-TaskOriginal (Join-Path $OutputDirectory 'kl079.product-contributors.rows.proposal.json') $contributors
Write-TaskOriginal (Join-Path $OutputDirectory 'kl079.unit-inventory.rows.proposal.json') $missingUnit
Write-TaskOriginal (Join-Path $OutputDirectory 'kl079.binding-review.json') ([ordered]@{
    sourceRevision = $SourceRevision; originalContractSha256 = Get-FcHash $contractPath
    githubAuthenticatedByThisScript = $false; acceptanceQualified = $false; coverageQualified = $false
    requiresAuthenticatedOriginalApiZipIntake = $true; requiresGuardedStrictRegistryJoin = $true
    requiresObservedWorkInExistingUnitFunctional02Group = $true
    requiresCanonicalExactGroupSelectorRegeneration = $true
    requiresActualExecutionAndUnchangedFinalVerifiers = $true })
