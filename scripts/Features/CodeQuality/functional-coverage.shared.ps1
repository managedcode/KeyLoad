$script:FunctionalCoverage = [ordered]@{
    SchemaVersion = 2
    CollectorVersion = '18.11.2'
    Module = 'KeyLoad.Query'
    ModuleFile = 'KeyLoad.Query.dll'
    ContractName = 'functional-coverage.contract.json'
    SettingsName = 'functional-coverage.settings.xml'
    ManifestName = 'functional-coverage.source-manifest.json'
    ReportJsonName = 'functional-coverage.report.json'
    ReportMarkdownName = 'functional-coverage.report.md'
    SettingsCopyName = 'functional-coverage.settings.xml'
    DeploymentDirectory = 'tests/KeyLoad.UnitTests/bin/Release/net10.0'
    SourceDirectory = 'src/KeyLoad.Query'
    SourceCount = 103
    ContributorSourceCount = 21
    ContributorClassCount = 10
    ContributorCaseCount = 25
    TestNamespace = 'KeyLoad.UnitTests.Features.QueryExecution'
    DllName = 'KeyLoad.Query.dll'
    PdbName = 'KeyLoad.Query.pdb'
    HitUnion = 'logical-or by source path and line number across the two reports'
    BranchMerge = 'unmeasured; retain each native report branch pair separately'
    MaxXmlBytes = 33554432
    MaxXmlCharacters = 33554432
    MaxSources = 5000
    MaxDistinctLineLocations = 100000
    RevisionPattern = '\A[0-9a-f]{40}\z'
    HexPattern = '\A[0-9a-f]{64}\z'
    PathSlash = '/'
    ErrorRepository = 'Repository must be an existing absolute directory.'
    ErrorEvidence = 'EvidenceRoot must be an absolute directory.'
    ErrorStale = 'Prepare refuses existing functional-coverage evidence.'
    ErrorContract = 'The frozen functional coverage contract is invalid.'
    ErrorInventory = 'KeyLoad.Query source inventory differs from the frozen contract.'
    ErrorDeployment = 'Release test deployment must contain the Query DLL and matching PDB.'
    ErrorManifest = 'Prepared functional coverage manifest is missing, stale, or invalid.'
    ErrorCoverage = 'A required native Cobertura report is missing or malformed.'
    ErrorTrx = 'PartitionQuery TRX cases differ from the exact admitted class, method, or instance inventory.'
    ErrorDrift = 'Source, module, PDB, settings, or script identity drifted during collection.'
    ErrorUnexpectedModule = 'Cobertura contains a module outside the frozen Query identity.'
    ErrorUnexpectedSource = 'Cobertura contains a source outside the frozen KeyLoad.Query inventory.'
    ErrorInvalidCount = 'Cobertura contains an invalid or conflicting line or branch count.'
    ErrorIncomplete = 'Native coverage evidence is absent or incomplete.'
    ErrorPath = 'A coverage path is unsafe or escapes the repository.'
    JsonSchema = 'schemaVersion'
    JsonSources = 'sources'
    JsonPath = 'path'
    JsonHash = 'sha256'
    JsonSourceRevision = 'sourceRevision'
    JsonRepository = 'repository'
    JsonContractHash = 'contractSha256'
    JsonSettingsHash = 'settingsSha256'
    JsonScripts = 'scripts'
    JsonDeployment = 'deployment'
    JsonRuntime = 'runtime'
    JsonPowerShell = 'pwshVersion'
    JsonRuns = 'runs'
    JsonLines = 'lines'
    JsonLineUnion = 'lineUnion'
    JsonBranches = 'nativeBranchesByRun'
    JsonQualification = 'qualification'
    JsonCovered = 'covered'
    JsonTotal = 'total'
    JsonUncovered = 'uncoveredLocations'
    JsonModule = 'module'
    JsonSuite = 'suite'
    JsonTests = 'partitionQueryTests'
}

function Get-FcHash([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-FcRevision([string] $Root) {
    $revision = (& git -C $Root rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $null -eq $revision) { throw 'Repository HEAD could not be resolved.' }
    $revision = $revision.Trim()
    $githubRevision = [Environment]::GetEnvironmentVariable('GITHUB_SHA')
    if (-not [string]::IsNullOrWhiteSpace($githubRevision) -and $githubRevision -cne $revision) {
        throw 'GITHUB_SHA differs from the selected repository HEAD.'
    }
    if ($revision -cnotmatch $script:FunctionalCoverage.RevisionPattern) {
        throw 'A lowercase 40-character source revision is required.'
    }
    $revision
}

function Resolve-FcPath([string] $Root, [string] $Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative.Split([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) -contains '..') {
        throw $script:FunctionalCoverage.ErrorPath
    }
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, $Relative))
    $back = [IO.Path]::GetRelativePath($Root, $full)
    if ([IO.Path]::IsPathRooted($back) -or $back -eq '..' -or $back.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw $script:FunctionalCoverage.ErrorPath
    }
    $current = $Root
    foreach ($segment in $Relative.Split([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw $script:FunctionalCoverage.ErrorPath }
        }
    }
    $full
}

function Write-FcJson([string] $Path, [object] $Value) {
    $temp = $Path + '.tmp'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($temp, (ConvertTo-Json -InputObject $Value -Depth 30), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

function Get-FcSourceInventory([string] $Root, [object] $Contract) {
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Contract.sources) {
        $path = [string] $source
        if (-not $expected.Add($path)) { throw $script:FunctionalCoverage.ErrorContract }
        $resolved = Resolve-FcPath $Root $path
        if (-not [IO.File]::Exists($resolved)) { throw $script:FunctionalCoverage.ErrorInventory }
    }
    $directory = Resolve-FcPath $Root $script:FunctionalCoverage.SourceDirectory
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.cs' -File -Recurse -Force) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $script:FunctionalCoverage.ErrorPath }
        if ($file.FullName -match '[\\/](obj|bin)[\\/]') { continue }
        $path = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace([IO.Path]::DirectorySeparatorChar, '/')
        if (-not $expected.Remove($path)) { throw $script:FunctionalCoverage.ErrorInventory }
    }
    if ($expected.Count -ne 0) { throw $script:FunctionalCoverage.ErrorInventory }
    @($Contract.sources | ForEach-Object {
        $path = [string] $_
        [ordered]@{ path = $path; sha256 = Get-FcHash (Resolve-FcPath $Root $path) }
    })
}

function Read-FcContract([string] $Path) {
    $contract = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    $t = $script:FunctionalCoverage
    if ($contract.schemaVersion -ne $t.SchemaVersion -or $contract.module -cne $t.Module -or
        $contract.moduleFile -cne $t.ModuleFile -or $contract.collector.version -cne $t.CollectorVersion -or
        $contract.collector.package -cne 'Microsoft.Testing.Extensions.CodeCoverage' -or
        $contract.collector.format -cne 'cobertura' -or $contract.sourceDirectory -cne $t.SourceDirectory -or
        (@($contract.requirements | Sort-Object) -join ',') -cne 'AC-CQ-018,AC-CQ-019,AC-CQ-039,REQ-CQ-009' -or
        $contract.deploymentDirectory -cne $t.DeploymentDirectory -or
        $contract.sources.Count -ne $t.SourceCount -or
        $contract.contributors.sourceFiles.Count -ne $t.ContributorSourceCount -or
        $contract.contributors.testClasses.Count -ne $t.ContributorClassCount -or
        $contract.contributors.testNamespace -cne $t.TestNamespace -or
        $contract.contributors.testNamePrefix -cne 'PartitionQuery' -or
        $contract.contributors.filter -cne '/*/*/PartitionQuery*/*' -or
        (@($contract.contributors.suiteNames) -join ',') -cne 'unit,unit-scalar' -or
        (@($contract.contributors.testClasses | Sort-Object) -join ',') -cne 'PartitionQueryAuthorizationTests,PartitionQueryCancellationTests,PartitionQueryContractTests,PartitionQueryGrantTests,PartitionQueryMcpContractTests,PartitionQueryMergeTests,PartitionQueryPublicAuthorizationTests,PartitionQueryPublicBudgetTests,PartitionQueryPublicContractTests,PartitionQueryPublicMergeTests' -or
        $contract.contributors.exactCases.Count -ne $t.ContributorCaseCount -or
        $contract.bounds.originalXmlBytes -ne 33554432 -or $contract.bounds.sources -ne 5000 -or
        $contract.bounds.distinctLineLocations -ne 100000 -or
        $null -ne $contract.thresholds -or
        $contract.reportMerge.branchMerge -cne $t.BranchMerge) { throw $t.ErrorContract }
    Assert-FcExpectedCases $contract
    $contract
}

function Assert-FcExpectedCases([object] $Contract) {
    $t = $script:FunctionalCoverage
    $classes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($class in $Contract.contributors.testClasses) { [void] $classes.Add([string] $class) }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $acceptanceRequirements = @{
        'AC-PQUERY-001' = 'REQ-PQUERY-001'; 'AC-PQUERY-002' = 'REQ-PQUERY-002'
        'AC-PQUERY-003' = 'REQ-PQUERY-003'; 'AC-PQUERY-004' = 'REQ-PQUERY-004'
        'AC-PQUERY-005' = 'REQ-PQUERY-005'; 'AC-PQUERY-006' = 'REQ-PQUERY-006'
    }
    foreach ($case in $Contract.contributors.exactCases) {
        $prefix = $Contract.contributors.testNamespace + '.'
        $className = [string] $case.className
        $methodName = [string] $case.methodName
        $instanceName = [string] $case.instanceName
        if ((@($case.Keys | Sort-Object) -join ',') -cne 'acceptance,className,executedModules,instanceName,methodName,operationOutcomeAndState,requirements') { throw $t.ErrorContract }
        if (-not $className.StartsWith($prefix, [StringComparison]::Ordinal) -or
            -not $classes.Contains($className.Substring($prefix.Length)) -or
            -not $className.Substring($prefix.Length).StartsWith([string] $Contract.contributors.testNamePrefix, [StringComparison]::Ordinal) -or
            $methodName -cnotmatch '\A[A-Za-z_][A-Za-z0-9_]*\z' -or
            $instanceName -cne $methodName -or
            [string]::IsNullOrWhiteSpace([string] $case.operationOutcomeAndState) -or
            (@($case.executedModules).Count -ne 1) -or $case.executedModules[0] -cne $t.Module -or
            (@($case.acceptance).Count -eq 0) -or (@($case.requirements).Count -eq 0) -or
            -not $seen.Add("$className|$methodName")) { throw $t.ErrorContract }
        $expectedRequirements = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($acceptance in $case.acceptance) {
            if (-not $acceptanceRequirements.ContainsKey([string] $acceptance)) { throw $t.ErrorContract }
            [void] $expectedRequirements.Add($acceptanceRequirements[[string] $acceptance])
        }
        $observedRequirements = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($requirement in $case.requirements) { [void] $observedRequirements.Add([string] $requirement) }
        if ($expectedRequirements.Count -ne @($case.requirements).Count -or
            @($case.acceptance).Count -ne (@($case.acceptance | Sort-Object -Unique)).Count -or
            -not $expectedRequirements.SetEquals($observedRequirements)) { throw $t.ErrorContract }
    }
    $admittedClasses = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $Contract.contributors.exactCases) {
        [void] $admittedClasses.Add(([string] $case.className).Substring($Contract.contributors.testNamespace.Length + 1))
    }
    if ($seen.Count -ne $t.ContributorCaseCount -or -not $admittedClasses.SetEquals($classes)) { throw $t.ErrorContract }
}
