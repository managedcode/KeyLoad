function Get-FcScriptInventory {
    @((Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'functional-coverage*.ps1' -File | Sort-Object Name) | ForEach-Object {
        [ordered]@{ name = $_.Name; sha256 = Get-FcHash $_.FullName }
    })
}

function Get-FcDeploymentInventory([string] $Root, [object] $Contract) {
    $directory = Resolve-FcPath $Root $Contract.deploymentDirectory
    $files = [Collections.Generic.List[object]]::new()
    foreach ($name in @($Contract.deploymentFiles)) {
        $path = Resolve-FcPath $Root "$($Contract.deploymentDirectory)/$name"
        if (-not [IO.File]::Exists($path)) { throw $script:FunctionalCoverage.ErrorDeployment }
        $files.Add([ordered]@{ name = $name; sha256 = Get-FcHash $path })
    }
    $files
}

function Get-FcContributorInventory([string] $Root, [object] $Contract) {
    $seenClasses = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($relative in $Contract.contributors.sourceFiles) {
        $path = [string] $relative
        if (-not $path.StartsWith('tests/KeyLoad.UnitTests/Features/QueryExecution/', [StringComparison]::Ordinal)) { throw $script:FunctionalCoverage.ErrorContract }
        $file = Resolve-FcPath $Root $path
        if (-not [IO.File]::Exists($file)) { throw $script:FunctionalCoverage.ErrorInventory }
        if ($path.Contains('/Cases/')) {
            $classes = @([regex]::Matches([IO.File]::ReadAllText($file), '\bclass\s+(PartitionQuery\w+Tests)') | ForEach-Object { $_.Groups[1].Value })
            foreach ($class in $classes) { [void] $seenClasses.Add($class) }
        }
        $entries.Add([ordered]@{ path = $path; sha256 = Get-FcHash $file })
    }
    if (-not $seenClasses.SetEquals([string[]] @($Contract.contributors.testClasses))) { throw $script:FunctionalCoverage.ErrorContract }
    @($entries)
}

function Get-FcSettings([string] $Root, [string] $TemplatePath, [string] $OutputPath, [object] $Contract) {
    $deployment = Resolve-FcPath $Root $Contract.deploymentDirectory
    $modulePath = [IO.Path]::GetFullPath((Join-Path $deployment $Contract.moduleFile))
    $escaped = [Security.SecurityElement]::Escape([regex]::Escape($modulePath))
    $xml = Get-Content -LiteralPath $TemplatePath -Raw
    $placeholder = '{{QUERY_MODULE_PATH_REGEX}}'
    if (-not $xml.Contains($placeholder)) { throw $script:FunctionalCoverage.ErrorContract }
    $xml = $xml.Replace($placeholder, $escaped)
    $parsed = [Xml.XmlDocument]::new()
    $parsed.XmlResolver = $null
    $parsed.LoadXml($xml)
    [IO.File]::WriteAllText($OutputPath, $xml, [Text.UTF8Encoding]::new($false))
    $OutputPath
}

function Resolve-FcEvidenceFile([string] $EvidenceRoot, [string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathRooted($Path)) { throw $script:FunctionalCoverage.ErrorIncomplete }
    $full = [IO.Path]::GetFullPath($Path)
    $relative = [IO.Path]::GetRelativePath($EvidenceRoot, $full)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
        $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
        -not [IO.File]::Exists($full)) { throw $script:FunctionalCoverage.ErrorIncomplete }
    $current = $EvidenceRoot
    foreach ($segment in $relative.Split([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))) {
        $current = Join-Path $current $segment
        if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw $script:FunctionalCoverage.ErrorPath }
    }
    $full
}

function Invoke-FcPrepare([string] $Root, [string] $EvidenceRoot, [string] $ContractPath, [string] $TemplatePath) {
    $t = $script:FunctionalCoverage
    foreach ($name in @($t.ManifestName,$t.ReportJsonName,$t.ReportMarkdownName,$t.SettingsCopyName)) {
        if ([IO.File]::Exists((Join-Path $EvidenceRoot $name))) { throw $t.ErrorStale }
    }
    foreach ($suite in @('unit','unit-scalar')) {
        if (Test-Path -LiteralPath (Join-Path $EvidenceRoot $suite)) { throw $t.ErrorStale }
    }
    $contract = Read-FcContract $ContractPath
    $sourceInventory = Get-FcSourceInventory $Root $contract
    $deployment = @(Get-FcDeploymentInventory $Root $contract)
    $settingsPath = Join-Path $EvidenceRoot $t.SettingsCopyName
    Get-FcSettings $Root $TemplatePath $settingsPath $contract | Out-Null
    $manifest = [ordered]@{
        schemaVersion = $t.SchemaVersion
        sourceRevision = Get-FcRevision $Root
        repository = $Root
        contractSha256 = Get-FcHash $ContractPath
        settingsSha256 = Get-FcHash $settingsPath
        sources = @($sourceInventory)
        contributors = @(Get-FcContributorInventory $Root $contract)
        deployment = @($deployment)
        scripts = @(Get-FcScriptInventory)
        settingsTemplateSha256 = Get-FcHash $TemplatePath
        runtime = [Environment]::Version.ToString()
        pwshVersion = $PSVersionTable.PSVersion.ToString()
        preparedUtc = [DateTime]::UtcNow.ToString('O')
    }
    Write-FcJson (Join-Path $EvidenceRoot $t.ManifestName) $manifest
}

function Assert-FcManifest([string] $Root, [string] $EvidenceRoot, [string] $ContractPath) {
    $t = $script:FunctionalCoverage
    $manifestPath = Join-Path $EvidenceRoot $t.ManifestName
    if (-not [IO.File]::Exists($manifestPath)) { throw $t.ErrorManifest }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $contract = Read-FcContract $ContractPath
    if ($manifest.schemaVersion -ne $t.SchemaVersion -or $manifest.repository -cne $Root -or
        $manifest.sourceRevision -cne (Get-FcRevision $Root) -or
        $manifest.contractSha256 -cne (Get-FcHash $ContractPath) -or
        $manifest.runtime -cne [Environment]::Version.ToString() -or
        $manifest.pwshVersion -cne $PSVersionTable.PSVersion.ToString() -or
        $manifest.settingsTemplateSha256 -cne (Get-FcHash (Join-Path $PSScriptRoot $t.SettingsName)) -or
        $manifest.settingsSha256 -cne (Get-FcHash (Join-Path $EvidenceRoot $t.SettingsCopyName))) { throw $t.ErrorManifest }
    $currentSources = ConvertTo-Json -InputObject @(Get-FcSourceInventory $Root $contract) -Compress -Depth 10
    $preparedSources = ConvertTo-Json -InputObject @($manifest.sources) -Compress -Depth 10
    $currentContributors = ConvertTo-Json -InputObject @(Get-FcContributorInventory $Root $contract) -Compress -Depth 10
    $preparedContributors = ConvertTo-Json -InputObject @($manifest.contributors) -Compress -Depth 10
    $currentDeployment = ConvertTo-Json -InputObject @(Get-FcDeploymentInventory $Root $contract) -Compress -Depth 10
    $preparedDeployment = ConvertTo-Json -InputObject @($manifest.deployment) -Compress -Depth 10
    $currentScripts = ConvertTo-Json -InputObject @(Get-FcScriptInventory) -Compress -Depth 10
    $preparedScripts = ConvertTo-Json -InputObject @($manifest.scripts) -Compress -Depth 10
    if ($currentSources -cne $preparedSources -or $currentContributors -cne $preparedContributors -or
        $currentDeployment -cne $preparedDeployment -or $currentScripts -cne $preparedScripts) {
        throw $t.ErrorDrift
    }
    $manifest
}

function Invoke-FcVerify([string] $Root, [string] $EvidenceRoot, [string] $ContractPath,
    [string] $UnitCobertura, [string] $UnitTrx, [int] $UnitExitCode,
    [string] $ScalarCobertura, [string] $ScalarTrx, [int] $ScalarExitCode, [string] $Filter) {
    $t = $script:FunctionalCoverage
    if ($UnitExitCode -ne 0 -or $ScalarExitCode -ne 0) { throw $t.ErrorIncomplete }
    $contract = Read-FcContract $ContractPath
    if ($Filter -cne $contract.contributors.filter) { throw $t.ErrorIncomplete }
    [void] (Assert-FcManifest $Root $EvidenceRoot $ContractPath)
    $sourceInventory = @(Get-FcSourceInventory $Root $contract)
    $runInputs = @(
        [ordered]@{ suite = 'unit'; coverage = $UnitCobertura; trx = $UnitTrx },
        [ordered]@{ suite = 'unit-scalar'; coverage = $ScalarCobertura; trx = $ScalarTrx }
    )
    $runs = [Collections.Generic.List[object]]::new()
    foreach ($input in $runInputs) {
        if ([string]::IsNullOrWhiteSpace($input.coverage) -or [string]::IsNullOrWhiteSpace($input.trx) -or
            -not [IO.Path]::IsPathRooted($input.coverage) -or -not [IO.Path]::IsPathRooted($input.trx)) { throw $t.ErrorIncomplete }
        $coveragePath = Resolve-FcEvidenceFile $EvidenceRoot $input.coverage
        $trxPath = Resolve-FcEvidenceFile $EvidenceRoot $input.trx
        $coverage = Read-FcCobertura $Root $coveragePath $sourceInventory
        $trx = Read-FcTrx $trxPath $input.suite @($contract.contributors.testClasses)
        $runs.Add([ordered]@{ trx = $trx; coverage = $coverage; reportHash = Get-FcHash $coveragePath; trxHash = Get-FcHash $trxPath })
    }
    $report = New-FcReport @($runs) $Filter
    Write-FcJson (Join-Path $EvidenceRoot $t.ReportJsonName) $report
    [IO.File]::WriteAllText((Join-Path $EvidenceRoot $t.ReportMarkdownName), (Convert-FcReportToMarkdown $report), [Text.UTF8Encoding]::new($false))
    if ($report.unionLineCounts.total -le 0) { throw $t.ErrorIncomplete }
}
