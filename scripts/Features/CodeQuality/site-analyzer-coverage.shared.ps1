$script:CoverageTokens = [ordered]@{
    SchemaVersion = 1
    RequiredSourceCount = 30
    RequiredConfigurationCount = 8
    RequiredExecutableCount = 25
    RequiredDeclarationCount = 5
    RequiredPipelineCount = 12
    RequiredModuleLinePercent = 80
    RequiredModuleBranchPercent = 70
    RequiredPipelineLinePercent = 90
    MaxXmlCharacters = 50000000
    ErrorActionStop = 'Stop'
    ModePrepare = 'Prepare'
    ModeVerify = 'Verify'
    ModuleName = 'KeyLoad.Analyzers'
    RequirementCodeQuality = 'REQ-CQ-006'
    AcceptanceCodeQuality = 'AC-CQ-009'
    RequirementBenchmark = 'REQ-BC-027'
    AcceptanceBenchmark = 'AC-BC-027'
    ExecutableKind = 'executable'
    DeclarationKind = 'declaration-only'
    HashAlgorithm = 'SHA256'
    HexPattern = '\A[0-9a-f]{64}\z'
    RevisionPattern = '\A[0-9a-f]{40}\z'
    RelativePathPattern = '\A(?!/)(?![A-Za-z]:)[^\x00]+\z'
    ParentPathSegment = '..'
    PathSeparator = [IO.Path]::DirectorySeparatorChar
    AlternatePathSeparator = [IO.Path]::AltDirectorySeparatorChar
    PathSlash = '/'
    PathBackslash = '\'
    PathColon = ':'
    FailureJoinSeparator = ', '
    RepositoryPathCompare = [StringComparison]::Ordinal
    SourceManifestName = 'source-manifest.json'
    DerivedReportName = 'report.json'
    CoberturaName = 'coverage.cobertura.xml'
    SettingsSourcePath = 'scripts/Features/CodeQuality/site-analyzer-coverage.settings.xml'
    SettingsCopyName = 'coverage.config.xml'
    HelperDirectory = 'scripts/Features/CodeQuality'
    HelperFilePattern = 'site-analyzer-coverage*.ps1'
    CSharpExtension = '.cs'
    ManifestTempSuffix = '.tmp'
    Utf8Encoding = [Text.UTF8Encoding]::new($false)
    XmlRoot = 'coverage'
    XmlPackageName = 'name'
    XmlClassName = 'name'
    XmlFilename = 'filename'
    XmlLineNumber = 'number'
    XmlHits = 'hits'
    XmlBranch = 'branch'
    XmlBranchTrue = 'true'
    XmlBranchFalse = 'false'
    XmlConditionCoverage = 'condition-coverage'
    BranchCoveragePattern = '\A(?:100(?:\.0{1,2})?|(?:0|[1-9][0-9]?)(?:\.[0-9]{1,2})?)% \(([0-9]+)/([0-9]+)\)\z'
    XmlPackageSelector = './packages/package'
    XmlSourceSelector = './sources/source'
    XmlClassSelector = './classes/class'
    XmlLineSelector = './lines/line'
    JsonSchemaVersion = 'schemaVersion'
    JsonRequirements = 'requirements'
    JsonModule = 'module'
    JsonCollectorVersion = 'collectorVersion'
    JsonSourceDirectory = 'sourceDirectory'
    JsonSources = 'sources'
    JsonConfiguration = 'configuration'
    JsonThresholds = 'thresholds'
    JsonPipelines = 'pipelines'
    JsonNativeFormat = 'nativeFormat'
    JsonPackageSelector = 'packageSelector'
    JsonPackageName = 'packageName'
    JsonNativeSourceRoots = 'sourceRoots'
    JsonNativeUnverifiedRoots = 'unverifiedDeterministicRoots'
    JsonNativeHitSemantics = 'hitSemantics'
    JsonLineSelector = 'lineSelector'
    JsonArtifactNames = 'artifactNames'
    JsonManifestArtifact = 'manifest'
    JsonReportArtifact = 'report'
    JsonNativeCoverageArtifact = 'nativeCoverage'
    JsonDiagnostic = 'diagnostic'
    JsonPath = 'path'
    JsonClassification = 'classification'
    JsonModuleLinePercent = 'moduleLinePercent'
    JsonModuleBranchPercent = 'moduleBranchPercent'
    JsonCriticalLinePercent = 'criticalPipelineLinePercent'
    JsonSourceRevision = 'sourceRevision'
    JsonRepositoryRoot = 'repositoryRoot'
    JsonContractHash = 'contractSha256'
    JsonSourcesHashList = 'sources'
    JsonConfigurationHashList = 'configuration'
    JsonGateScripts = 'gateScripts'
    JsonSettingsConfigPath = 'settingsConfigPath'
    JsonSettingsConfigHash = 'settingsConfigSha256'
    JsonRuntimeVersion = 'runtimeVersion'
    JsonPowerShellVersion = 'pwshVersion'
    JsonSha256 = 'sha256'
    JsonNativeReportHash = 'nativeReportSha256'
    JsonFiles = 'files'
    JsonCriticalPipelines = 'criticalPipelines'
    JsonLinesCovered = 'linesCovered'
    JsonLinesValid = 'linesValid'
    JsonBranchesCovered = 'branchesCovered'
    JsonBranchesValid = 'branchesValid'
    JsonThresholdsUsed = 'thresholds'
    JsonPassed = 'passed'
    JsonFailures = 'failures'
    ValueCovered = 'covered'
    ValueTotal = 'total'
    ValueHits = 'hits'
    ValueBranch = 'branch'
    ValuePassed = 'passed'
    ValueLine = 'line'
    ValueIdentity = 'identity'
    ValueSourceKinds = 'sourceKinds'
    ValueLineUnion = 'lineUnion'
    ValueBranchRecords = 'branchRecords'
    ValueRepository = 'repository'
    ValueContract = 'contract'
    ValueEvidence = 'evidence'
    ValueManifest = 'manifest'
    FailurePrefix = 'Coverage gate failed: '
    FailureModuleLines = 'module line threshold'
    FailureModuleBranches = 'module branch threshold'
    ErrorInvalidMode = 'Mode must be Prepare or Verify.'
    ErrorNoRevision = 'Coverage source revision must be a lowercase 40-character commit SHA.'
    ErrorNoRepository = 'Repository must be an existing absolute directory.'
    ErrorNoEvidence = 'EvidenceRoot must be an absolute directory.'
    ErrorNoContract = 'Contract must be an existing absolute file.'
    ErrorStaleEvidence = 'Prepare refuses existing manifest, report, or Cobertura evidence.'
    ErrorMissingManifest = 'Verify requires a prepared source manifest.'
    ErrorMissingCobertura = 'Verify requires the native Cobertura report.'
    ErrorInvalidContract = 'Frozen coverage contract is invalid.'
    ErrorInvalidManifest = 'Prepared source manifest does not match the current candidate.'
    ErrorUnsafePath = 'Contract or Cobertura path is unsafe or outside the repository.'
    ErrorMissingSource = 'Required source or configuration file is missing.'
    ErrorSettingsCopy = 'Coverage settings copy is missing or differs from the frozen source settings.'
    ErrorMalformedCobertura = 'Cobertura report is malformed or contains ambiguous counts.'
    ErrorEmptyDenominator = 'Coverage denominator is empty.'
    ErrorInvalidCounter = 'Coverage count is not a valid nonnegative integer.'
    ErrorDuplicateConflict = 'Duplicate coverage identity has conflicting counts.'
    ErrorNoClassName = 'Cobertura class identity is empty.'
    ErrorNoModule = 'Cobertura must contain exactly one matching analyzer module.'
    ErrorUnknownSource = 'Cobertura maps to an unknown or declaration-only source.'
    ErrorMissingLine = 'Cobertura source contains no executable line records.'
    ErrorInvalidBranch = 'Cobertura branch coverage pair is invalid.'
    ErrorInvalidReport = 'Coverage report input must be an existing absolute file.'
    ErrorPercentBounds = 'Coverage percent numerator exceeds its denominator.'
    ErrorCounterOverflow = 'Coverage counter aggregation overflowed Int64.'
    ErrorLineBeyondSource = 'Cobertura line number exceeds the hash-verified physical source file.'
    ErrorInventoryCount = 'Contract must inventory exactly 25 executable and 5 declaration-only sources.'
    ErrorInvalidPipeline = 'Critical pipeline inventory must cover every executable source exactly by path.'
    ErrorSourceInventory = 'Analyzer source directory contents differ from the frozen source inventory.'
    NativePackageSelector = '/coverage/packages/package'
    NativeLineSelector = 'classes/class/lines/line'
    NativeSourceRoots = 'captured-checkout-or-explicit-unique-source-root'
    NativeUnverifiedRoots = 'fail-closed'
    NativeHitSemantics = 'non-negative integer; covered iff positive; source-line union across distinct class identities; conflicting identical identity fails'
    NativeCollectorVersion = '18.11.2'
    SiteSourceRevisionVariable = 'KEYLOAD_SITE_SOURCE_REVISION'
    GitHubRevisionVariable = 'GITHUB_SHA'
}

function Get-CoverageSourceRevision {
    $tokens = $script:CoverageTokens
    $environment = [Environment]::GetEnvironmentVariables()
    if ($environment.Contains($tokens.SiteSourceRevisionVariable)) {
        $revision = [Environment]::GetEnvironmentVariable($tokens.SiteSourceRevisionVariable)
    }
    else {
        $revision = [Environment]::GetEnvironmentVariable($tokens.GitHubRevisionVariable)
    }

    if ($revision -cnotmatch $tokens.RevisionPattern) { throw $tokens.ErrorNoRevision }
    $revision
}

function Get-CoverageSha256([string] $LiteralPath) {
    (Get-FileHash -LiteralPath $LiteralPath -Algorithm $script:CoverageTokens.HashAlgorithm).Hash.ToLowerInvariant()
}

function Resolve-CoverageRepositoryFile([string] $RepositoryRoot, [string] $RelativePath) {
    $tokens = $script:CoverageTokens
    if ($RelativePath -notmatch $tokens.RelativePathPattern -or
        $RelativePath.Split([char[]]@($tokens.PathSeparator, $tokens.AlternatePathSeparator)) -contains $tokens.ParentPathSegment) {
        throw $tokens.ErrorUnsafePath
    }

    $fullPath = [IO.Path]::GetFullPath([IO.Path]::Combine($RepositoryRoot, $RelativePath))
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $fullPath)
    if ($relative.StartsWith($tokens.ParentPathSegment + [string] $tokens.PathSeparator, $tokens.RepositoryPathCompare) -or
        [IO.Path]::IsPathRooted($relative)) {
        throw $tokens.ErrorUnsafePath
    }

    $fullPath
}

function Assert-CoveragePathHasNoLinks([string] $RepositoryRoot, [string] $RelativePath) {
    $tokens = $script:CoverageTokens
    $currentPath = $RepositoryRoot
    $segments = $RelativePath.Split([char[]]@($tokens.PathSeparator, $tokens.AlternatePathSeparator))
    foreach ($segment in $segments) {
        $currentPath = [IO.Path]::Combine($currentPath, $segment)
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw $tokens.ErrorUnsafePath }
        }
    }
}

function Write-CoverageJson([string] $LiteralPath, [object] $Value) {
    $directory = [IO.Path]::GetDirectoryName($LiteralPath)
    $tempPath = $LiteralPath + $script:CoverageTokens.ManifestTempSuffix
    $json = ConvertTo-Json -InputObject $Value -Depth 30
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText($tempPath, $json, $script:CoverageTokens.Utf8Encoding)
    Move-Item -LiteralPath $tempPath -Destination $LiteralPath -Force
}
