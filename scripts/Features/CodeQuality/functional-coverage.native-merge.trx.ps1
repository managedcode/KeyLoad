$script:FcNativeTrx = [ordered]@{ Invalid = 'An original native test outcome report does not match the exact contributor inventory.' }

function Read-FcNativeTrx([string] $Path, [string] $Suite, [object[]] $ExpectedCases) {
    $doc = Read-FcXml $Path 'TestRun'
    $expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($case in $ExpectedCases) { $expected.Add((Get-FcNativeTrxKey $case.className $case.methodName $case.instanceName), $case) }
    $definitions = Read-FcNativeTrxDefinitions $doc $expected
    $results = Read-FcNativeTrxResults $doc $definitions $expected
    Assert-FcNativeTrxCounters $doc $definitions.Count $results.Count
    [ordered]@{ suite = $Suite; testCount = $results.Count; cases = @($results | Sort-Object identity) }
}

function Get-FcNativeTrxKey([string] $ClassName, [string] $MethodName, [string] $InstanceName) {
    "$ClassName|$MethodName|$InstanceName"
}

function Read-FcNativeTrxDefinitions([Xml.XmlDocument] $Document, [Collections.Generic.Dictionary[string, object]] $Expected) {
    $definitions = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $nodes = @($Document.SelectNodes('//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]'))
    foreach ($node in $nodes) {
        $id = $node.GetAttribute('id'); $method = $node.SelectSingleNode('./*[local-name()="TestMethod"]')
        if ([string]::IsNullOrWhiteSpace($id) -or $null -eq $method -or $definitions.ContainsKey($id)) { throw $script:FcNativeTrx.Invalid }
        $className = $method.GetAttribute('className'); $methodName = $method.GetAttribute('name')
        $prefix = "$className|$methodName|"
        $matches = @($Expected.Keys | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) })
        if ($matches.Count -eq 0 -or $node.GetAttribute('name') -cne $methodName) { throw $script:FcNativeTrx.Invalid }
        $definitions.Add($id, [ordered]@{ className = $className; methodName = $methodName })
    }
    if ($definitions.Count -eq 0) { throw $script:FcNativeTrx.Invalid }
    $definitions
}

function Read-FcNativeTrxResults([Xml.XmlDocument] $Document,
    [Collections.Generic.Dictionary[string, object]] $Definitions, [Collections.Generic.Dictionary[string, object]] $Expected) {
    $results = [Collections.Generic.List[object]]::new()
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $cases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($result in @($Document.SelectNodes('//*[local-name()="Results"]/*[local-name()="UnitTestResult"]'))) {
        $id = $result.GetAttribute('testId'); $testName = $result.GetAttribute('testName')
        if (-not $Definitions.ContainsKey($id) -or -not $ids.Add($id) -or $result.GetAttribute('outcome') -cne 'Passed') {
            throw $script:FcNativeTrx.Invalid
        }
        $definition = $Definitions[$id]
        $key = Get-FcNativeTrxKey $definition.className $definition.methodName $testName
        if (-not $Expected.ContainsKey($key) -or -not $cases.Add($key)) { throw $script:FcNativeTrx.Invalid }
        $results.Add([ordered]@{ identity = "$id|$($definition.className)|$($definition.methodName)|$testName"
            caseIdentity = $key
            testId = $id; className = $definition.className; methodName = $definition.methodName; instanceName = $testName })
    }
    if ($cases.Count -ne $Expected.Count -or $ids.Count -ne $Definitions.Count) { throw $script:FcNativeTrx.Invalid }
    @($results)
}

function Assert-FcNativeTrxCounters([Xml.XmlDocument] $Document, [int] $DefinitionCount, [int] $ResultCount) {
    $counters = $Document.SelectSingleNode('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
    if ($null -eq $counters -or (Convert-FcCount $counters.GetAttribute('total')) -ne $DefinitionCount -or
        (Convert-FcCount $counters.GetAttribute('executed')) -ne $ResultCount -or
        (Convert-FcCount $counters.GetAttribute('passed')) -ne $ResultCount) { throw $script:FcNativeTrx.Invalid }
    foreach ($name in @('failed','error','timeout','aborted','inconclusive','notRunnable','notExecuted',
            'disconnected','passedButRunAborted','inProgress','pending')) {
        $value = $counters.GetAttribute($name)
        if ($value.Length -gt 0 -and (Convert-FcCount $value) -ne 0) { throw $script:FcNativeTrx.Invalid }
    }
}
