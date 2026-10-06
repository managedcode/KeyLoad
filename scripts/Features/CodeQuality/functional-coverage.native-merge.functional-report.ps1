$script:FcNativeFunctionalReport = [ordered]@{
    Assemblies = [ordered]@{ unit = 'KeyLoad.UnitTests'; 'unit-scalar' = 'KeyLoad.UnitTests'; recovery = 'KeyLoad.RecoveryTests'; rf3 = 'KeyLoad.IntegrationTests' }
    Rf3Fixture = 'KeyLoad.IntegrationTests.ClusterFixture'
    Rf3ClassAliases = [ordered]@{
        'KeyLoad.IntegrationTests.Features.QueryExecution|PartitionQueryPublicRf3Tests(ClusterFixture)' = 'KeyLoad.IntegrationTests.Features.QueryExecution.PartitionQueryPublicRf3Tests'
        'KeyLoad.IntegrationTests.Features.DocumentStorage|McpDocumentCrudParityTests(ClusterFixture)' = 'KeyLoad.IntegrationTests.Features.DocumentStorage.McpDocumentCrudParityTests'
    }
    Invalid = 'The original native TUnit report does not match its contributors or has an unsuccessful outcome.'
    RootRequired = @('schemaVersion','assemblyName','machineName','timestamp','tunitVersion','operatingSystem',
        'runtimeVersion','totalDurationMs','summary','groups','spans')
    RootOptional = @('publicationGeneration','commitSha','branch','repositorySlug','sourceLinks')
    TestRequired = @('id','displayName','methodName','className','status','durationMs','startTime','endTime',
        'filePath','lineNumber','endLineNumber','traceId','spanId')
}

function Read-FcNativeFunctionalCases([object] $Report, [object[]] $ExpectedCases, [string] $Suite) {
    Assert-FcNativeFunctionalReport $Report $Suite
    $expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($case in $ExpectedCases) {
        $key = Get-FcNativeTrxKey ([string] $case.className) ([string] $case.methodName) ([string] $case.instanceName)
        $expected.Add($key, $case)
    }
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $identities = [Collections.Generic.List[string]]::new()
    foreach ($group in $Report.groups) {
        Assert-FcNativeFunctionalGroup $group
        $displayClass = [string] $group.namespace + '|' + [string] $group.className
        $qualifiedClass = [string] $group.namespace + '.' + [string] $group.className
        if ($Suite -ceq 'rf3') {
            if (-not $script:FcNativeFunctionalReport.Rf3ClassAliases.Contains($displayClass)) { throw $script:FcNativeFunctionalReport.Invalid }
            $qualifiedClass = [string] $script:FcNativeFunctionalReport.Rf3ClassAliases[$displayClass]
        }
        foreach ($test in $group.tests) {
            Assert-FcNativeFunctionalTest $test
            if ($test.className -cne $group.className) { throw $script:FcNativeFunctionalReport.Invalid }
            if ($Suite -ceq 'rf3') {
                $nativeId = $qualifiedClass + '(' + $script:FcNativeFunctionalReport.Rf3Fixture + ').1.1.' +
                    $test.methodName + '.1.1.0'
                if ($test.id -cne $nativeId) { throw $script:FcNativeFunctionalReport.Invalid }
            }
            $key = Get-FcNativeTrxKey $qualifiedClass ([string] $test.methodName) ([string] $test.displayName)
            if (-not $expected.ContainsKey($key) -or -not $observed.Add($key)) {
                throw $script:FcNativeFunctionalReport.Invalid
            }
            $identities.Add($key)
        }
    }
    if ($identities.Count -ne $expected.Count -or $Report.summary.total -ne $identities.Count) {
        throw $script:FcNativeFunctionalReport.Invalid
    }
    @($identities | Sort-Object)
}

function Assert-FcNativeFunctionalReport([object] $Report, [string] $Suite) {
    if ($Report -isnot [Collections.IDictionary] -or $Report.schemaVersion -ne 1 -or
        -not $script:FcNativeFunctionalReport.Assemblies.Contains($Suite) -or
        $Report.assemblyName -cne $script:FcNativeFunctionalReport.Assemblies[$Suite] -or
        [string]::IsNullOrWhiteSpace($Report.machineName) -or [string]::IsNullOrWhiteSpace($Report.timestamp) -or
        [string]::IsNullOrWhiteSpace($Report.tunitVersion) -or [string]::IsNullOrWhiteSpace($Report.operatingSystem) -or
        [string]::IsNullOrWhiteSpace($Report.runtimeVersion) -or $Report.groups -isnot [array] -or
        $Report.groups.Count -eq 0 -or $Report.summary -isnot [Collections.IDictionary] -or
        $Report.totalDurationMs -isnot [ValueType] -or -not [double]::IsFinite([double] $Report.totalDurationMs) -or
        $Report.totalDurationMs -lt 0) {
        throw $script:FcNativeFunctionalReport.Invalid
    }
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $script:FcNativeFunctionalReport.RootRequired) { [void] $allowed.Add($name) }
    foreach ($name in $script:FcNativeFunctionalReport.RootOptional) { [void] $allowed.Add($name) }
    foreach ($name in $Report.Keys) { if (-not $allowed.Contains([string] $name)) { throw $script:FcNativeFunctionalReport.Invalid } }
    foreach ($name in $script:FcNativeFunctionalReport.RootRequired) { if (-not $Report.Contains($name)) { throw $script:FcNativeFunctionalReport.Invalid } }
    foreach ($name in @('publicationGeneration','commitSha','branch','repositorySlug')) {
        if ($Report.Contains($name) -and ($Report[$name] -isnot [string] -or [string]::IsNullOrWhiteSpace($Report[$name]))) {
            throw $script:FcNativeFunctionalReport.Invalid
        }
    }
    if ($Report.Contains('publicationGeneration') -and $Report.publicationGeneration -cnotmatch '\A(?:[0-9a-fA-F]{32}|[0-9a-fA-F-]{36})\z') {
        throw $script:FcNativeFunctionalReport.Invalid
    }
    if ($Report.Contains('commitSha') -and $Report.commitSha -cnotmatch '\A[0-9a-f]{40}\z') {
        throw $script:FcNativeFunctionalReport.Invalid
    }
    if ($Report.Contains('repositorySlug') -and $Report.repositorySlug -cne 'managedcode/KeyLoad') {
        throw $script:FcNativeFunctionalReport.Invalid
    }
    if ($Report.Contains('sourceLinks')) {
        Assert-FcNativeExactKeys $Report.sourceLinks @('lineUrl','rangeUrl')
        if ([string]::IsNullOrWhiteSpace($Report.sourceLinks.lineUrl) -or
            [string]::IsNullOrWhiteSpace($Report.sourceLinks.rangeUrl)) { throw $script:FcNativeFunctionalReport.Invalid }
    }
    Assert-FcNativeFunctionalSummary $Report.summary
}

function Assert-FcNativeFunctionalGroup([object] $Group) {
    Assert-FcNativeExactKeys $Group @('className','namespace','summary','tests')
    if ([string]::IsNullOrWhiteSpace($Group.className) -or [string]::IsNullOrWhiteSpace($Group.namespace) -or
        $Group.tests -isnot [array] -or $Group.tests.Count -eq 0) { throw $script:FcNativeFunctionalReport.Invalid }
    Assert-FcNativeFunctionalSummary $Group.summary
    if ($Group.summary.total -ne $Group.tests.Count) { throw $script:FcNativeFunctionalReport.Invalid }
}

function Assert-FcNativeFunctionalTest([object] $Test) {
    $required = $script:FcNativeFunctionalReport.TestRequired
    $optional = @('sourceRelativePath')
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $required) { [void] $allowed.Add($name) }
    foreach ($name in $optional) { [void] $allowed.Add($name) }
    if ($Test -isnot [Collections.IDictionary] -or $Test.Keys.Count -lt $required.Count -or $Test.Keys.Count -gt $allowed.Count) {
        throw $script:FcNativeFunctionalReport.Invalid
    }
    foreach ($name in $Test.Keys) { if (-not $allowed.Contains([string] $name)) { throw $script:FcNativeFunctionalReport.Invalid } }
    foreach ($name in $required) { if (-not $Test.Contains($name)) { throw $script:FcNativeFunctionalReport.Invalid } }
    foreach ($name in @('id','displayName','methodName','className','status','startTime','endTime','filePath','traceId','spanId')) {
        if ($Test[$name] -isnot [string] -or [string]::IsNullOrWhiteSpace($Test[$name])) {
            throw $script:FcNativeFunctionalReport.Invalid
        }
    }
    if ($Test.status -cne 'passed' -or $Test.durationMs -isnot [ValueType] -or
        -not [double]::IsFinite([double] $Test.durationMs) -or $Test.durationMs -lt 0 -or
        $Test.lineNumber -isnot [int] -and $Test.lineNumber -isnot [long] -or
        $Test.endLineNumber -isnot [int] -and $Test.endLineNumber -isnot [long] -or
        $Test.lineNumber -le 0 -or $Test.endLineNumber -lt $Test.lineNumber -or
        ($Test.Contains('sourceRelativePath') -and ($Test.sourceRelativePath -isnot [string] -or [string]::IsNullOrWhiteSpace($Test.sourceRelativePath)))) {
        throw $script:FcNativeFunctionalReport.Invalid
    }
}

function Assert-FcNativeFunctionalSummary([object] $Summary) {
    Assert-FcNativeExactKeys $Summary @('total','passed','failed','skipped','cancelled','timedOut','flaky')
    foreach ($name in @('total','passed','failed','skipped','cancelled','timedOut','flaky')) {
        if (($Summary[$name] -isnot [int] -and $Summary[$name] -isnot [long]) -or $Summary[$name] -lt 0) {
            throw $script:FcNativeFunctionalReport.Invalid
        }
    }
    if ($Summary.failed -ne 0 -or $Summary.skipped -ne 0 -or $Summary.cancelled -ne 0 -or
        $Summary.timedOut -ne 0 -or $Summary.flaky -ne 0 -or $Summary.passed -ne $Summary.total) {
        throw $script:FcNativeFunctionalReport.Invalid
    }
}
