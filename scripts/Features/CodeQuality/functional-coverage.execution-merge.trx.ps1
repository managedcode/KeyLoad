$script:RuntimeFcTrx = [ordered]@{ Invalid = 'An original execution test outcome report does not match the exact contributor inventory.' }

function Read-RuntimeFcTrx([string] $Path, [string] $Suite, [object[]] $ExpectedCases) {
    $doc = Read-FcXml $Path 'TestRun'
    $expected = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($case in $ExpectedCases) { $expected.Add((Get-FcTrxKey $case.className $case.methodName $case.instanceName), $case) }
    $definitions = Read-FcTrxDefinitions $doc $expected
    $results = @(Read-FcTrxResults $doc $definitions $expected)
    Assert-FcTrxCounters $doc $definitions.Count $results.Count
    [ordered]@{ suite = $Suite; testCount = $results.Count; cases = @($results | Sort-Object identity) }
}

function Get-FcTrxKey([string] $ClassName, [string] $MethodName, [string] $InstanceName) {
    "$ClassName|$MethodName|$InstanceName"
}

function Read-FcTrxDefinitions([Xml.XmlDocument] $Document, [Collections.Generic.Dictionary[string, object]] $Expected) {
    $definitions = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $nodes = @($Document.SelectNodes('//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]'))
    foreach ($node in $nodes) {
        $id = $node.GetAttribute('id'); $method = $node.SelectSingleNode('./*[local-name()="TestMethod"]')
        if ([string]::IsNullOrWhiteSpace($id) -or $null -eq $method -or $definitions.ContainsKey($id)) { throw $script:RuntimeFcTrx.Invalid }
        $className = $method.GetAttribute('className'); $methodName = $method.GetAttribute('name')
        $instanceName = $node.GetAttribute('name')
        $key = Get-FcTrxKey $className $methodName $instanceName
        if (-not $Expected.ContainsKey($key)) { throw $script:RuntimeFcTrx.Invalid }
        $definitions.Add($id, [ordered]@{ className = $className; methodName = $methodName; instanceName = $instanceName })
    }
    if ($definitions.Count -eq 0) { throw $script:RuntimeFcTrx.Invalid }
    $definitions
}

function Read-FcTrxResults([Xml.XmlDocument] $Document,
    [Collections.Generic.Dictionary[string, object]] $Definitions, [Collections.Generic.Dictionary[string, object]] $Expected) {
    $results = [Collections.Generic.List[object]]::new()
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $cases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($result in @($Document.SelectNodes('//*[local-name()="Results"]/*[local-name()="UnitTestResult"]'))) {
        $id = $result.GetAttribute('testId'); $testName = $result.GetAttribute('testName')
        if (-not $Definitions.ContainsKey($id) -or -not $ids.Add($id) -or $result.GetAttribute('outcome') -cne 'Passed') {
            throw $script:RuntimeFcTrx.Invalid
        }
        $definition = $Definitions[$id]
        $key = Get-FcTrxKey $definition.className $definition.methodName $testName
        if ($testName -cne $definition.instanceName -or -not $Expected.ContainsKey($key) -or -not $cases.Add($key)) { throw $script:RuntimeFcTrx.Invalid }
        $results.Add([ordered]@{ identity = "$id|$($definition.className)|$($definition.methodName)|$testName"
            caseIdentity = $key
            testId = $id; className = $definition.className; methodName = $definition.methodName; instanceName = $testName })
    }
    if ($cases.Count -ne $Expected.Count -or $ids.Count -ne $Definitions.Count) { throw $script:RuntimeFcTrx.Invalid }
    @($results)
}

function Assert-FcTrxCounters([Xml.XmlDocument] $Document, [int] $DefinitionCount, [int] $ResultCount) {
    $counters = $Document.SelectSingleNode('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
    if ($null -eq $counters -or (Convert-FcCount $counters.GetAttribute('total')) -ne $DefinitionCount -or
        (Convert-FcCount $counters.GetAttribute('executed')) -ne $ResultCount -or
        (Convert-FcCount $counters.GetAttribute('passed')) -ne $ResultCount) { throw $script:RuntimeFcTrx.Invalid }
    foreach ($name in @('failed','error','timeout','aborted','inconclusive','notRunnable','notExecuted',
            'disconnected','passedButRunAborted','inProgress','pending')) {
        $value = $counters.GetAttribute($name)
        if ($value.Length -gt 0 -and (Convert-FcCount $value) -ne 0) { throw $script:RuntimeFcTrx.Invalid }
    }
}
