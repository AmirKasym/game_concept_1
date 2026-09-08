$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The standalone tests require the Windows .NET Framework 4 C# compiler.'
}
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$testExePath = Join-Path $resultsPath 'ShipSimulationTests.exe'
& $compilerPath /nologo /target:exe "/out:$testExePath" (Join-Path $projectRoot 'Assets/TradeWinds/Core/ShipSimulation.cs') (Join-Path $projectRoot 'Assets/TradeWinds/Core/DeckMotor.cs') (Join-Path $projectRoot 'Tests/ShipSimulationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
& $testExePath | Tee-Object -FilePath (Join-Path $resultsPath 'core-tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed. See TestResults/core-tests.txt.' }
