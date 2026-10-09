param([Parameter(Mandatory = $true)][string] $Path)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../CodeQuality/functional-coverage.shared.ps1')
. (Join-Path $PSScriptRoot '../CodeQuality/functional-coverage.parse.ps1')
. (Join-Path $PSScriptRoot '../CodeQuality/functional-coverage.native-merge.trx.ps1')
$method = 'MillionAcknowledgedDocumentsRemainCorrectDuringSdkAndOfficialMcpReads'
$expected = @([ordered]@{
    className = 'KeyLoad.IntegrationTests.Features.DocumentStorage.HeavyDocumentLoadRf3Tests'
    methodName = $method
    instanceName = $method
})
try {
    $result = Read-FcNativeTrx $Path 'rf3-heavy-load' $expected
    if ($result.testCount -ne 1) { throw 'The functional gate requires exactly one original test.' }
    $result | ConvertTo-Json -Depth 8 -Compress
} catch {
    [Console]::Error.WriteLine('Original functional load TRX rejected.')
    exit 1
}
