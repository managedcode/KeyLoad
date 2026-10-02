function Read-CoverageContract([string] $ContractPath, [string] $RepositoryRoot) {
    $tokens = $script:CoverageTokens
    $contract = Get-Content -LiteralPath $ContractPath -Raw | ConvertFrom-Json -AsHashtable
    $nativeFormat = $contract[$tokens.JsonNativeFormat]
    $artifacts = $contract[$tokens.JsonArtifactNames]
    if ($contract[$tokens.JsonSchemaVersion] -ne $tokens.SchemaVersion -or
        $contract[$tokens.JsonModule] -cne $tokens.ModuleName -or
        $contract[$tokens.JsonCollectorVersion] -cne $tokens.NativeCollectorVersion -or
        $contract[$tokens.JsonRequirements] -cnotcontains $tokens.RequirementCodeQuality -or
        $contract[$tokens.JsonRequirements] -cnotcontains $tokens.AcceptanceCodeQuality -or
        $contract[$tokens.JsonRequirements] -cnotcontains $tokens.RequirementBenchmark -or
        $contract[$tokens.JsonRequirements] -cnotcontains $tokens.AcceptanceBenchmark -or
        $contract[$tokens.JsonSources].Count -ne $tokens.RequiredSourceCount -or
        $contract[$tokens.JsonConfiguration].Count -ne $tokens.RequiredConfigurationCount -or
        $contract[$tokens.JsonThresholds][$tokens.JsonModuleLinePercent] -ne $tokens.RequiredModuleLinePercent -or
        $contract[$tokens.JsonThresholds][$tokens.JsonModuleBranchPercent] -ne $tokens.RequiredModuleBranchPercent -or
        $contract[$tokens.JsonThresholds][$tokens.JsonCriticalLinePercent] -ne $tokens.RequiredPipelineLinePercent -or
        $nativeFormat[$tokens.JsonPackageSelector] -cne $tokens.NativePackageSelector -or
        $nativeFormat[$tokens.JsonLineSelector] -cne $tokens.NativeLineSelector -or
        $nativeFormat[$tokens.JsonPackageName] -cne $tokens.ModuleName -or
        $nativeFormat[$tokens.JsonNativeSourceRoots] -cne $tokens.NativeSourceRoots -or
        $nativeFormat[$tokens.JsonNativeUnverifiedRoots] -cne $tokens.NativeUnverifiedRoots -or
        $nativeFormat[$tokens.JsonNativeHitSemantics] -cne $tokens.NativeHitSemantics -or
        $artifacts[$tokens.JsonManifestArtifact] -cne $tokens.SourceManifestName -or
        $artifacts[$tokens.JsonReportArtifact] -cne $tokens.DerivedReportName -or
        $artifacts[$tokens.JsonNativeCoverageArtifact] -cne $tokens.CoberturaName) {
        throw $tokens.ErrorInvalidContract
    }

    $executables = @($contract[$tokens.JsonSources] | Where-Object { $_[$tokens.JsonClassification] -ceq $tokens.ExecutableKind })
    $declarations = @($contract[$tokens.JsonSources] | Where-Object { $_[$tokens.JsonClassification] -ceq $tokens.DeclarationKind })
    if ($executables.Count -ne $tokens.RequiredExecutableCount -or
        $declarations.Count -ne $tokens.RequiredDeclarationCount -or
        $contract[$tokens.JsonPipelines].Count -ne $tokens.RequiredPipelineCount) {
        throw $tokens.ErrorInventoryCount
    }
    Assert-CoveragePipelineInventory $contract $executables
    Assert-CoverageSourceFileInventory $RepositoryRoot $contract

    $contract
}

function Assert-CoverageSourceFileInventory([string] $RepositoryRoot, [object] $Contract) {
    $tokens = $script:CoverageTokens
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Contract[$tokens.JsonSources]) {
        if (-not $expected.Add([string] $source[$tokens.JsonPath])) { throw $tokens.ErrorSourceInventory }
    }

    $sourceRoot = Resolve-CoverageRepositoryFile $RepositoryRoot ([string] $Contract[$tokens.JsonSourceDirectory])
    Assert-CoveragePathHasNoLinks $RepositoryRoot ([string] $Contract[$tokens.JsonSourceDirectory])
    $entries = @(Get-ChildItem -LiteralPath $sourceRoot -Force -Recurse)
    foreach ($sourceFile in $entries) {
        if (($sourceFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $tokens.ErrorUnsafePath }
        if ($sourceFile.PSIsContainer -or $sourceFile.Extension -cne $tokens.CSharpExtension) { continue }
        $path = ([IO.Path]::GetRelativePath($RepositoryRoot, $sourceFile.FullName)).Replace([string] $tokens.PathSeparator, $tokens.PathSlash).Replace([string] $tokens.PathBackslash, $tokens.PathSlash)
        if (-not $expected.Remove($path)) { throw $tokens.ErrorSourceInventory }
    }

    if ($expected.Count -ne 0) { throw $tokens.ErrorSourceInventory }
}

function Assert-CoveragePipelineInventory([object] $Contract, [object[]] $Executables) {
    $tokens = $script:CoverageTokens
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $diagnostics = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Executables) { [void] $expected.Add([string] $source[$tokens.JsonPath]) }
    foreach ($pipeline in $Contract[$tokens.JsonPipelines]) {
        $diagnostic = [string] $pipeline[$tokens.JsonDiagnostic]
        if ([string]::IsNullOrWhiteSpace($diagnostic) -or -not $diagnostics.Add($diagnostic)) { throw $tokens.ErrorInvalidPipeline }
        foreach ($path in $pipeline[$tokens.JsonSources]) {
            if (-not $expected.Contains([string] $path)) { throw $tokens.ErrorInvalidPipeline }
            [void] $observed.Add([string] $path)
        }
    }

    if (-not $expected.SetEquals($observed)) { throw $tokens.ErrorInvalidPipeline }
}

function Get-ProjectFileInventory([string] $RepositoryRoot, [object] $Contract) {
    $tokens = $script:CoverageTokens
    $sources = [Collections.Generic.List[object]]::new()
    $configuration = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($source in $Contract[$tokens.JsonSources]) {
        $path = [string] $source[$tokens.JsonPath]
        if (-not $seen.Add($path)) { throw $tokens.ErrorInvalidContract }
        $file = Resolve-CoverageRepositoryFile $RepositoryRoot $path
        Assert-CoveragePathHasNoLinks $RepositoryRoot $path
        if (-not [IO.File]::Exists($file)) { throw $tokens.ErrorMissingSource }
        $entry = [ordered]@{}
        $entry[$tokens.JsonPath] = $path
        $entry[$tokens.JsonClassification] = [string] $source[$tokens.JsonClassification]
        $entry[$tokens.JsonSha256] = Get-CoverageSha256 $file
        $sources.Add($entry)
    }

    foreach ($configPath in $Contract[$tokens.JsonConfiguration]) {
        $path = [string] $configPath
        if (-not $seen.Add($path)) { throw $tokens.ErrorInvalidContract }
        $file = Resolve-CoverageRepositoryFile $RepositoryRoot $path
        Assert-CoveragePathHasNoLinks $RepositoryRoot $path
        if (-not [IO.File]::Exists($file)) { throw $tokens.ErrorMissingSource }
        $entry = [ordered]@{}
        $entry[$tokens.JsonPath] = $path
        $entry[$tokens.JsonSha256] = Get-CoverageSha256 $file
        $configuration.Add($entry)
    }

    $result = [ordered]@{}
    $result[$tokens.JsonSources] = @($sources)
    $result[$tokens.JsonConfiguration] = @($configuration)
    $result
}

function Get-GateToolInventory([string] $RepositoryRoot, [string] $EvidenceRoot) {
    $tokens = $script:CoverageTokens
    $gateScripts = [Collections.Generic.List[object]]::new()
    $helperDirectory = Resolve-CoverageRepositoryFile $RepositoryRoot $tokens.HelperDirectory
    Assert-CoveragePathHasNoLinks $RepositoryRoot $tokens.HelperDirectory
    $helpers = @(Get-ChildItem -LiteralPath $helperDirectory -Filter $tokens.HelperFilePattern -File | Sort-Object -Property Name)
    if ($helpers.Count -eq 0) { throw $tokens.ErrorInvalidContract }
    foreach ($helper in $helpers) {
        $path = [IO.Path]::GetRelativePath($RepositoryRoot, $helper.FullName).Replace([string] $tokens.PathSeparator, $tokens.PathSlash)
        Assert-CoveragePathHasNoLinks $RepositoryRoot $path
        $entry = [ordered]@{}
        $entry[$tokens.JsonPath] = $path
        $entry[$tokens.JsonSha256] = Get-CoverageSha256 $helper.FullName
        $gateScripts.Add($entry)
    }

    $settingsSource = Resolve-CoverageRepositoryFile $RepositoryRoot $tokens.SettingsSourcePath
    Assert-CoveragePathHasNoLinks $RepositoryRoot $tokens.SettingsSourcePath
    $settingsCopy = Join-Path $EvidenceRoot $tokens.SettingsCopyName
    if (-not [IO.File]::Exists($settingsCopy) -or
        (Get-CoverageSha256 $settingsSource) -cne (Get-CoverageSha256 $settingsCopy)) {
        throw $tokens.ErrorSettingsCopy
    }

    $inventory = [ordered]@{}
    $inventory[$tokens.JsonGateScripts] = @($gateScripts)
    $inventory[$tokens.JsonSettingsConfigPath] = $settingsCopy
    $inventory[$tokens.JsonSettingsConfigHash] = Get-CoverageSha256 $settingsCopy
    $inventory
}

function Get-CoverageInventory([string] $RepositoryRoot, [object] $Contract, [string] $EvidenceRoot) {
    $projectFiles = Get-ProjectFileInventory $RepositoryRoot $Contract
    $gateTools = Get-GateToolInventory $RepositoryRoot $EvidenceRoot
    $inventory = [ordered]@{}
    $inventory[$script:CoverageTokens.JsonSources] = $projectFiles[$script:CoverageTokens.JsonSources]
    $inventory[$script:CoverageTokens.JsonConfiguration] = $projectFiles[$script:CoverageTokens.JsonConfiguration]
    $inventory[$script:CoverageTokens.JsonGateScripts] = $gateTools[$script:CoverageTokens.JsonGateScripts]
    $inventory[$script:CoverageTokens.JsonSettingsConfigPath] = $gateTools[$script:CoverageTokens.JsonSettingsConfigPath]
    $inventory[$script:CoverageTokens.JsonSettingsConfigHash] = $gateTools[$script:CoverageTokens.JsonSettingsConfigHash]
    $inventory
}

function Invoke-CoveragePrepare([string] $RepositoryRoot, [string] $ContractPath, [string] $EvidenceRoot) {
    $tokens = $script:CoverageTokens
    $manifestPath = Join-Path $EvidenceRoot $tokens.SourceManifestName
    $reportPath = Join-Path $EvidenceRoot $tokens.DerivedReportName
    $rawReportPath = Join-Path $EvidenceRoot $tokens.CoberturaName
    if ([IO.File]::Exists($manifestPath) -or [IO.File]::Exists($reportPath) -or [IO.File]::Exists($rawReportPath)) {
        throw $tokens.ErrorStaleEvidence
    }

    $revision = [string] $env:GITHUB_SHA
    if ($revision -cnotmatch $tokens.RevisionPattern) { throw $tokens.ErrorNoRevision }
    $contract = Read-CoverageContract $ContractPath $RepositoryRoot
    $inventory = Get-CoverageInventory $RepositoryRoot $contract $EvidenceRoot
    $manifest = [ordered]@{}
    $manifest[$tokens.JsonSchemaVersion] = $tokens.SchemaVersion
    $manifest[$tokens.JsonSourceRevision] = $revision
    $manifest[$tokens.JsonRepositoryRoot] = $RepositoryRoot
    $manifest[$tokens.JsonContractHash] = Get-CoverageSha256 $ContractPath
    $manifest[$tokens.JsonSourcesHashList] = $inventory[$tokens.JsonSources]
    $manifest[$tokens.JsonConfigurationHashList] = $inventory[$tokens.JsonConfiguration]
    $manifest[$tokens.JsonGateScripts] = $inventory[$tokens.JsonGateScripts]
    $manifest[$tokens.JsonSettingsConfigPath] = $inventory[$tokens.JsonSettingsConfigPath]
    $manifest[$tokens.JsonSettingsConfigHash] = $inventory[$tokens.JsonSettingsConfigHash]
    $manifest[$tokens.JsonRuntimeVersion] = [Environment]::Version.ToString()
    $manifest[$tokens.JsonPowerShellVersion] = $PSVersionTable.PSVersion.ToString()
    Write-CoverageJson $manifestPath $manifest
}

function Read-AndVerifyCoverageManifest([string] $RepositoryRoot, [string] $ContractPath, [string] $EvidenceRoot) {
    $tokens = $script:CoverageTokens
    $manifestPath = Join-Path $EvidenceRoot $tokens.SourceManifestName
    if (-not [IO.File]::Exists($manifestPath)) { throw $tokens.ErrorMissingManifest }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $revision = [string] $env:GITHUB_SHA
    $runtimeVersion = [Environment]::Version.ToString()
    $powerShellVersion = $PSVersionTable.PSVersion.ToString()
    if ($manifest[$tokens.JsonSchemaVersion] -ne $tokens.SchemaVersion -or
        $revision -cnotmatch $tokens.RevisionPattern -or
        $manifest[$tokens.JsonSourceRevision] -cne $revision -or
        $manifest[$tokens.JsonRepositoryRoot] -cne $RepositoryRoot -or
        $manifest[$tokens.JsonContractHash] -cne (Get-CoverageSha256 $ContractPath) -or
        $manifest[$tokens.JsonRuntimeVersion] -cne $runtimeVersion -or
        $manifest[$tokens.JsonPowerShellVersion] -cne $powerShellVersion) {
        throw $tokens.ErrorInvalidManifest
    }

    $contract = Read-CoverageContract $ContractPath $RepositoryRoot
    $inventory = Get-CoverageInventory $RepositoryRoot $contract $EvidenceRoot
    $currentSources = ConvertTo-Json -InputObject @($inventory[$tokens.JsonSources]) -Compress -Depth 10
    $preparedSources = ConvertTo-Json -InputObject @($manifest[$tokens.JsonSourcesHashList]) -Compress -Depth 10
    $currentConfiguration = ConvertTo-Json -InputObject @($inventory[$tokens.JsonConfiguration]) -Compress -Depth 10
    $preparedConfiguration = ConvertTo-Json -InputObject @($manifest[$tokens.JsonConfigurationHashList]) -Compress -Depth 10
    $currentGateScripts = ConvertTo-Json -InputObject @($inventory[$tokens.JsonGateScripts]) -Compress -Depth 10
    $preparedGateScripts = ConvertTo-Json -InputObject @($manifest[$tokens.JsonGateScripts]) -Compress -Depth 10
    if ($currentSources -cne $preparedSources -or
        $currentConfiguration -cne $preparedConfiguration -or
        $currentGateScripts -cne $preparedGateScripts -or
        $manifest[$tokens.JsonSettingsConfigPath] -cne $inventory[$tokens.JsonSettingsConfigPath] -or
        $manifest[$tokens.JsonSettingsConfigHash] -cne $inventory[$tokens.JsonSettingsConfigHash]) {
        throw $tokens.ErrorInvalidManifest
    }

    $prepared = [ordered]@{}
    $prepared[$tokens.ValueContract] = $contract
    $prepared[$tokens.ValueManifest] = $manifest
    $prepared
}
