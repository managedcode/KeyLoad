[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Mode,
    [Parameter(Mandatory = $true)][string] $Repository,
    [Parameter(Mandatory = $true)][string] $Contract,
    [Parameter(Mandatory = $true)][string] $EvidenceRoot,
    [Parameter(Mandatory = $false)][string] $CoverageReport
)

. (Join-Path $PSScriptRoot 'site-analyzer-coverage.shared.ps1')
. (Join-Path $PSScriptRoot 'site-analyzer-coverage.inventory.ps1')
. (Join-Path $PSScriptRoot 'site-analyzer-coverage.parse.ps1')
$ErrorActionPreference = $script:CoverageTokens.ErrorActionStop

function Resolve-CoverageArguments {
    $tokens = $script:CoverageTokens
    if (-not [IO.Path]::IsPathRooted($Repository) -or -not [IO.Directory]::Exists($Repository)) { throw $tokens.ErrorNoRepository }
    if (-not [IO.Path]::IsPathRooted($Contract) -or -not [IO.File]::Exists($Contract)) { throw $tokens.ErrorNoContract }
    if (-not [IO.Path]::IsPathRooted($EvidenceRoot)) { throw $tokens.ErrorNoEvidence }
    $repositoryRoot = [IO.Path]::GetFullPath($Repository).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $contractPath = [IO.Path]::GetFullPath($Contract)
    $evidencePath = [IO.Path]::GetFullPath($EvidenceRoot)
    [IO.Directory]::CreateDirectory($evidencePath) | Out-Null
    $paths = [ordered]@{}
    $paths[$tokens.ValueRepository] = $repositoryRoot
    $paths[$tokens.ValueContract] = $contractPath
    $paths[$tokens.ValueEvidence] = $evidencePath
    $paths
}

function Write-CoverageFailureEvidence([object] $Paths, [object] $Contract, [string] $Revision, [string] $Reason) {
    $tokens = $script:CoverageTokens
    try {
        $coverageHash = $null
        if (-not [string]::IsNullOrWhiteSpace($CoverageReport) -and
            [IO.Path]::IsPathRooted($CoverageReport) -and
            [IO.File]::Exists($CoverageReport)) {
            $coverageHash = Get-CoverageSha256 ([IO.Path]::GetFullPath($CoverageReport))
        }

        $safeRevision = $null
        if ($Revision -cmatch $tokens.RevisionPattern) { $safeRevision = $Revision }
        $failed = New-CoberturaFailureReport $safeRevision $coverageHash $Contract $Reason
        Write-CoverageJson (Join-Path $Paths[$tokens.ValueEvidence] $tokens.DerivedReportName) $failed
    }
    catch [System.Exception] {
        # Failure-report persistence must never mask the original verification error.
    }
}

function Invoke-CoverageVerify([object] $Paths) {
    $tokens = $script:CoverageTokens
    $contract = $null
    $revision = [string] $env:GITHUB_SHA
    $derivedReportWritten = $false
    try {
        $contract = Read-CoverageContract $Paths[$tokens.ValueContract] $Paths[$tokens.ValueRepository]
        if ([string]::IsNullOrWhiteSpace($CoverageReport) -or -not [IO.Path]::IsPathRooted($CoverageReport)) {
            throw $tokens.ErrorInvalidReport
        }

        $reportPath = [IO.Path]::GetFullPath($CoverageReport)
        if (-not [IO.File]::Exists($reportPath)) { throw $tokens.ErrorMissingCobertura }
        $prepared = Read-AndVerifyCoverageManifest $Paths[$tokens.ValueRepository] $Paths[$tokens.ValueContract] $Paths[$tokens.ValueEvidence]
        $contract = $prepared[$tokens.ValueContract]
        $reportHash = Get-CoverageSha256 $reportPath
        $document = Read-CoberturaDocument $reportPath
        $records = Read-CoberturaRecords $document $Paths[$tokens.ValueRepository] $contract
        $files = Get-FileCoverageRows $records $contract
        $pipelines = Get-PipelineCoverageRows $files $contract
        $result = New-CoverageReport $files $pipelines $contract $revision $reportHash
        Write-CoverageJson (Join-Path $Paths[$tokens.ValueEvidence] $tokens.DerivedReportName) $result
        $derivedReportWritten = $true
        if (-not $result[$tokens.JsonPassed]) { throw ($tokens.FailurePrefix + ($result[$tokens.JsonFailures] -join $tokens.FailureJoinSeparator)) }
    }
    catch [System.Exception] {
        if (-not $derivedReportWritten) {
            Write-CoverageFailureEvidence $Paths $contract $revision $_.Exception.Message
        }

        throw
    }
}

try {
    if ($Mode -cne $script:CoverageTokens.ModePrepare -and $Mode -cne $script:CoverageTokens.ModeVerify) {
        throw $script:CoverageTokens.ErrorInvalidMode
    }

    $paths = Resolve-CoverageArguments
    if ($Mode -ceq $script:CoverageTokens.ModePrepare) {
        Invoke-CoveragePrepare $paths[$script:CoverageTokens.ValueRepository] $paths[$script:CoverageTokens.ValueContract] $paths[$script:CoverageTokens.ValueEvidence]
    }
    else {
        Invoke-CoverageVerify $paths
    }
}
catch [System.Exception] {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
