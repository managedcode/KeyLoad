$script:FcFunctionalReport = [ordered]@{
    Assemblies = [ordered]@{ unit = 'KeyLoad.FunctionalTests'; recovery = 'KeyLoad.ProcessRecoveryTests'; cluster = 'KeyLoad.ClusterTests' }
    ClusterFixture = 'KeyLoad.ClusterTests.ClusterFixture'
    ClusterClassAliases = [ordered]@{
        'KeyLoad.ClusterTests.QueryExecution|PartitionQueryPublicClusterTests(ClusterFixture)' = 'KeyLoad.ClusterTests.QueryExecution.PartitionQueryPublicClusterTests'
        'KeyLoad.ClusterTests.DocumentStorage|McpDocumentCrudParityTests(ClusterFixture)' = 'KeyLoad.ClusterTests.DocumentStorage.McpDocumentCrudParityTests'
        'KeyLoad.ClusterTests.RelationalStorage|RelationalSqlClusterJoinTests(ClusterFixture)' = 'KeyLoad.ClusterTests.RelationalStorage.RelationalSqlClusterJoinTests'
        'KeyLoad.ClusterTests.RelationalStorage|RelationalSqlClusterJoinAuthorizationTests(ClusterFixture)' = 'KeyLoad.ClusterTests.RelationalStorage.RelationalSqlClusterJoinAuthorizationTests'
        'KeyLoad.ClusterTests.RelationalStorage|RelationalSqlClusterJoinCancellationTests(ClusterFixture)' = 'KeyLoad.ClusterTests.RelationalStorage.RelationalSqlClusterJoinCancellationTests'
        'KeyLoad.ClusterTests.RelationalStorage|RelationalSqlClusterJoinReadCutTests(ClusterFixture)' = 'KeyLoad.ClusterTests.RelationalStorage.RelationalSqlClusterJoinReadCutTests'
    }
    Invalid = 'The original execution TUnit report does not match its contributors or has an unsuccessful outcome.'
    RootRequired = @('schemaVersion','assemblyName','machineName','timestamp','tunitVersion','operatingSystem',
        'runtimeVersion','totalDurationMs','summary','groups','spans')
    RootOptional = @('publicationGeneration','commitSha','branch','repositorySlug','sourceLinks')
    TestRequired = @('id','displayName','methodName','className','status','durationMs','startTime','endTime',
        'filePath','lineNumber','endLineNumber','traceId','spanId')
}

function Read-FcFunctionalCases([object] $Report, [object[]] $ExpectedCases, [string] $Suite,
    [string] $Repository, [switch] $MatchSourceLocations,
    [Collections.Generic.Dictionary[string, string]] $ObservedIds,
    [Collections.Generic.Dictionary[string, string]] $RequiredIds) {
    $baseSuite = Get-FcBaseSuite $Suite
    Assert-FcFunctionalReport $Report $baseSuite
    $expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($case in $ExpectedCases) {
        $key = Get-FcTrxKey ([string] $case.className) ([string] $case.methodName) ([string] $case.instanceName)
        $expected.Add($key, $case)
    }
    $reportExpected = New-FcReportIdentityMap $ExpectedCases $MatchSourceLocations
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $runtimeIds = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $uniqueIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($null -ne $ObservedIds -and $ObservedIds.Count -ne 0) {
        throw $script:FcFunctionalReport.Invalid
    }
    $identities = [Collections.Generic.List[string]]::new()
    foreach ($group in $Report.groups) {
        Assert-FcFunctionalGroup $group
        $displayClass = [string] $group.namespace + '|' + [string] $group.className
        $qualifiedClass = [string] $group.namespace + '.' + [string] $group.className
        $clusterHasConstructorData = $false
        if ($baseSuite -ceq 'cluster') {
            if ($script:FcFunctionalReport.ClusterClassAliases.Contains($displayClass)) {
                $qualifiedClass = [string] $script:FcFunctionalReport.ClusterClassAliases[$displayClass]
                $clusterHasConstructorData = $true
            }
            else { throw $script:FcFunctionalReport.Invalid }
        }
        $groupQualifiedClass = $qualifiedClass
        foreach ($test in $group.tests) {
            $qualifiedClass = $groupQualifiedClass
            Assert-FcFunctionalTest $test
            if (-not $uniqueIds.Add([string] $test.id)) { throw $script:FcFunctionalReport.Invalid }
            if ($test.className -cne $group.className) { throw $script:FcFunctionalReport.Invalid }
            if ($baseSuite -ceq 'cluster') {
                if ($clusterHasConstructorData) {
                    $runtimeIdCore = $qualifiedClass + '(' + $script:FcFunctionalReport.ClusterFixture + ').1.1.' +
                        $test.methodName + '.1.1.0'
                }
                else {
                    $runtimeIdCore = $qualifiedClass + '.1.1.' + $test.methodName + '.1.1.0'
                }
                if ($test.id -cne $runtimeIdCore) { throw $script:FcFunctionalReport.Invalid }
            }
            if ($MatchSourceLocations) {
                $displayKey = Get-FcTrxKey $qualifiedClass ([string] $test.methodName) ([string] $test.displayName)
                if (-not $reportExpected.ContainsKey($displayKey)) { throw $script:FcFunctionalReport.Invalid }
                $qualifiedClass = [string] $reportExpected[$displayKey].className
            }
            $key = Get-FcTrxKey $qualifiedClass ([string] $test.methodName) ([string] $test.displayName)
            if (-not $expected.ContainsKey($key) -or -not $observed.Add($key)) {
                throw $script:FcFunctionalReport.Invalid
            }
            if ($MatchSourceLocations) {
                Assert-FcFunctionalSourceLocation $test $expected[$key] $Repository
            }
            if ($null -ne $RequiredIds -and
                (-not $RequiredIds.ContainsKey($key) -or $RequiredIds[$key] -cne $test.id)) {
                throw $script:FcFunctionalReport.Invalid
            }
            $runtimeIds.Add($key, [string] $test.id)
            $identities.Add($key)
        }
    }
    if ($identities.Count -ne $expected.Count -or $Report.summary.total -ne $identities.Count) {
        throw $script:FcFunctionalReport.Invalid
    }
    if ($null -ne $ObservedIds) {
        foreach ($key in $runtimeIds.Keys) { $ObservedIds.Add($key, $runtimeIds[$key]) }
    }
    @($identities | Sort-Object)
}

function Assert-FcFunctionalSourceLocation([object] $Test, [object] $Expected, [string] $Repository) {
    if ([string]::IsNullOrWhiteSpace($Repository) -or -not [IO.Path]::IsPathFullyQualified([string] $Test.filePath)) {
        throw $script:FcFunctionalReport.Invalid
    }
    $repositoryRoot = [IO.Path]::GetFullPath($Repository)
    $fullPath = [IO.Path]::GetFullPath([string] $Test.filePath)
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $fullPath).Replace([IO.Path]::DirectorySeparatorChar, '/')
    $mapped = [string] $script:FcCompiledIdentity.DeterministicPathMapPrefix + [string] $Expected.sourcePath
    if ($Test.filePath -ceq $mapped) { $relativePath = [string] $Expected.sourcePath }
    if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath -eq '..' -or
        $relativePath.StartsWith('../', [StringComparison]::Ordinal) -or
        $relativePath -cne $Expected.sourcePath -or $Test.lineNumber -ne $Expected.lineNumber -or
        $Test.endLineNumber -ne $Expected.endLineNumber -or
        ($Test.Contains('sourceRelativePath') -and $Test.sourceRelativePath -cne $Expected.sourcePath)) {
        throw $script:FcFunctionalReport.Invalid
    }
}

function Assert-FcFunctionalReport([object] $Report, [string] $Suite) {
    $baseSuite = Get-FcBaseSuite $Suite
    if ($Report -isnot [Collections.IDictionary] -or $Report.schemaVersion -ne 1 -or
        -not $script:FcFunctionalReport.Assemblies.Contains($baseSuite) -or
        $Report.assemblyName -cne $script:FcFunctionalReport.Assemblies[$baseSuite] -or
        [string]::IsNullOrWhiteSpace($Report.machineName) -or [string]::IsNullOrWhiteSpace($Report.timestamp) -or
        [string]::IsNullOrWhiteSpace($Report.tunitVersion) -or [string]::IsNullOrWhiteSpace($Report.operatingSystem) -or
        [string]::IsNullOrWhiteSpace($Report.runtimeVersion) -or $Report.groups -isnot [array] -or
        $Report.groups.Count -eq 0 -or $Report.summary -isnot [Collections.IDictionary] -or
        $Report.totalDurationMs -isnot [ValueType] -or -not [double]::IsFinite([double] $Report.totalDurationMs) -or
        $Report.totalDurationMs -lt 0) {
        throw $script:FcFunctionalReport.Invalid
    }
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $script:FcFunctionalReport.RootRequired) { [void] $allowed.Add($name) }
    foreach ($name in $script:FcFunctionalReport.RootOptional) { [void] $allowed.Add($name) }
    foreach ($name in $Report.Keys) { if (-not $allowed.Contains([string] $name)) { throw $script:FcFunctionalReport.Invalid } }
    foreach ($name in $script:FcFunctionalReport.RootRequired) { if (-not $Report.Contains($name)) { throw $script:FcFunctionalReport.Invalid } }
    foreach ($name in @('publicationGeneration','commitSha','branch','repositorySlug')) {
        if ($Report.Contains($name) -and ($Report[$name] -isnot [string] -or [string]::IsNullOrWhiteSpace($Report[$name]))) {
            throw $script:FcFunctionalReport.Invalid
        }
    }
    if ($Report.Contains('publicationGeneration') -and $Report.publicationGeneration -cnotmatch '\A(?:[0-9a-fA-F]{32}|[0-9a-fA-F-]{36})\z') {
        throw $script:FcFunctionalReport.Invalid
    }
    if ($Report.Contains('commitSha') -and $Report.commitSha -cnotmatch '\A[0-9a-f]{40}\z') {
        throw $script:FcFunctionalReport.Invalid
    }
    if ($Report.Contains('repositorySlug') -and $Report.repositorySlug -cne 'managedcode/KeyLoad') {
        throw $script:FcFunctionalReport.Invalid
    }
    if ($Report.Contains('sourceLinks')) {
        $requiredLinks = @('lineUrl','rangeUrl')
        $expectedLinks = if ($Report.sourceLinks -is [Collections.IDictionary] -and
            $Report.sourceLinks.Contains('rawUrl')) { @('lineUrl','rangeUrl','rawUrl') } else { $requiredLinks }
        Assert-FcExactKeys $Report.sourceLinks $expectedLinks
        foreach ($name in $expectedLinks) {
            if ($Report.sourceLinks[$name] -isnot [string] -or
                [string]::IsNullOrWhiteSpace($Report.sourceLinks[$name])) {
                throw $script:FcFunctionalReport.Invalid
            }
        }
    }
    Assert-FcFunctionalSummary $Report.summary
}

function Assert-FcFunctionalGroup([object] $Group) {
    Assert-FcExactKeys $Group @('className','namespace','summary','tests')
    if ([string]::IsNullOrWhiteSpace($Group.className) -or [string]::IsNullOrWhiteSpace($Group.namespace) -or
        $Group.tests -isnot [array] -or $Group.tests.Count -eq 0) { throw $script:FcFunctionalReport.Invalid }
    Assert-FcFunctionalSummary $Group.summary
    if ($Group.summary.total -ne $Group.tests.Count) { throw $script:FcFunctionalReport.Invalid }
}

function Assert-FcFunctionalTest([object] $Test) {
    $required = $script:FcFunctionalReport.TestRequired
    $optional = @('sourceRelativePath')
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $required) { [void] $allowed.Add($name) }
    foreach ($name in $optional) { [void] $allowed.Add($name) }
    if ($Test -isnot [Collections.IDictionary] -or $Test.Keys.Count -lt $required.Count -or $Test.Keys.Count -gt $allowed.Count) {
        throw $script:FcFunctionalReport.Invalid
    }
    foreach ($name in $Test.Keys) { if (-not $allowed.Contains([string] $name)) { throw $script:FcFunctionalReport.Invalid } }
    foreach ($name in $required) { if (-not $Test.Contains($name)) { throw $script:FcFunctionalReport.Invalid } }
    foreach ($name in @('id','displayName','methodName','className','status','startTime','endTime','filePath','traceId','spanId')) {
        if ($Test[$name] -isnot [string] -or [string]::IsNullOrWhiteSpace($Test[$name])) {
            throw $script:FcFunctionalReport.Invalid
        }
    }
    if ($Test.status -cne 'passed' -or $Test.durationMs -isnot [ValueType] -or
        -not [double]::IsFinite([double] $Test.durationMs) -or $Test.durationMs -lt 0 -or
        ($Test.lineNumber -isnot [int] -and $Test.lineNumber -isnot [long]) -or
        ($Test.endLineNumber -isnot [int] -and $Test.endLineNumber -isnot [long]) -or
        $Test.lineNumber -le 0 -or $Test.endLineNumber -lt $Test.lineNumber -or
        ($Test.Contains('sourceRelativePath') -and ($Test.sourceRelativePath -isnot [string] -or [string]::IsNullOrWhiteSpace($Test.sourceRelativePath)))) {
        throw $script:FcFunctionalReport.Invalid
    }
}

function Assert-FcFunctionalSummary([object] $Summary) {
    Assert-FcExactKeys $Summary @('total','passed','failed','skipped','cancelled','timedOut','flaky')
    foreach ($name in @('total','passed','failed','skipped','cancelled','timedOut','flaky')) {
        if (($Summary[$name] -isnot [int] -and $Summary[$name] -isnot [long]) -or $Summary[$name] -lt 0) {
            throw $script:FcFunctionalReport.Invalid
        }
    }
    if ($Summary.failed -ne 0 -or $Summary.skipped -ne 0 -or $Summary.cancelled -ne 0 -or
        $Summary.timedOut -ne 0 -or $Summary.flaky -ne 0 -or $Summary.passed -ne $Summary.total) {
        throw $script:FcFunctionalReport.Invalid
    }
}

function New-FcReportIdentityMap([object[]] $ExpectedCases, [bool] $MatchSourceLocations) {
    $reportExpected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    if ($MatchSourceLocations) {
        foreach ($case in $ExpectedCases) {
            $displayKey = Get-FcTrxKey $case.reportClassName $case.methodName $case.instanceName
            $reportExpected.Add($displayKey, $case)
        }
    }
    $reportExpected
}
