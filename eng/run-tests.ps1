[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    # A fresh directory prevents reports from earlier runs masking missing tests.
    $results = Join-Path 'test-results' ("unit-" + [guid]::NewGuid())
    & dotnet test Broiler.Documents.slnx -c $Configuration --no-build --nologo `
        --logger 'trx;LogFilePrefix=broiler' --results-directory $results
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
    $executed = 0
    foreach ($report in Get-ChildItem -LiteralPath $results -Filter *.trx) {
        [xml] $trx = Get-Content -LiteralPath $report.FullName -Raw
        $counters = $trx.SelectSingleNode('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
        if (!$counters) { throw "No test counters in $($report.FullName)." }
        $executed += [int] $counters.executed
    }
    Write-Host "Executed $executed tests."
    if ($executed -lt 1400) { throw "Executed test count collapsed to $executed; expected at least 1400." }
} finally { Pop-Location }
