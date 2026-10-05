[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare','Verify')][string] $Mode,
    [Parameter(Mandatory = $true)][string] $Repository,
    [Parameter(Mandatory = $true)][string] $EvidenceRoot,
    [string] $UnitCobertura,
    [string] $UnitTrx,
    [int] $UnitExitCode = -1,
    [string] $ScalarCobertura,
    [string] $ScalarTrx,
    [int] $ScalarExitCode = -1,
    [string] $Filter
)

. (Join-Path $PSScriptRoot 'functional-coverage.shared.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.compiled-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.test-identity.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.image-manifests.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.inventory.ps1')
. (Join-Path $PSScriptRoot 'functional-coverage.parse.ps1')
$ErrorActionPreference = 'Stop'

function Resolve-FcArguments {
    if (-not [IO.Path]::IsPathRooted($Repository) -or -not [IO.Directory]::Exists($Repository)) {
        throw $script:FunctionalCoverage.ErrorRepository
    }
    if (-not [IO.Path]::IsPathRooted($EvidenceRoot)) { throw $script:FunctionalCoverage.ErrorEvidence }
    $root = [IO.Path]::GetFullPath($Repository).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $evidence = [IO.Path]::GetFullPath($EvidenceRoot)
    $current = [IO.Path]::GetPathRoot($evidence)
    foreach ($segment in $evidence.Substring($current.Length).Split([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if ((Test-Path -LiteralPath $current -PathType Container) -and
            (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw $script:FunctionalCoverage.ErrorPath
        }
    }
    [IO.Directory]::CreateDirectory($evidence) | Out-Null
    $contract = Join-Path $PSScriptRoot $script:FunctionalCoverage.ContractName
    $settings = Join-Path $PSScriptRoot $script:FunctionalCoverage.SettingsName
    if (-not [IO.File]::Exists($contract) -or -not [IO.File]::Exists($settings)) { throw $script:FunctionalCoverage.ErrorContract }
    [ordered]@{ root = $root; evidence = $evidence; contract = $contract; settings = $settings }
}

try {
    $paths = Resolve-FcArguments
    if ($Mode -ceq 'Prepare') {
        Invoke-FcPrepare $paths.root $paths.evidence $paths.contract $paths.settings
    }
    else {
        Invoke-FcVerify $paths.root $paths.evidence $paths.contract `
            $UnitCobertura $UnitTrx $UnitExitCode $ScalarCobertura $ScalarTrx $ScalarExitCode $Filter
    }
}
catch [System.Exception] {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
