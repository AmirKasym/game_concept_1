param([switch]$DownloadOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$statusPath = Join-Path $resultsPath 'setup-status.json'
$lockPath = Join-Path $resultsPath 'setup.lock'
$setupLock = $null

function Write-SetupStatus([string]$step, [string]$message) {
    @{ step=$step; message=$message; updatedAt=[DateTime]::UtcNow.ToString('o'); processId=$PID } |
        ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
    Write-Output "$step : $message"
}

try {
    $setupLock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
    $version = '6000.0.62f1'
    $installerPath = Join-Path $resultsPath "UnitySetup64-$version.exe"
    $installRoot = Join-Path $env:LOCALAPPDATA "Unity/Editors/$version"
    $editorPath = Join-Path $installRoot 'Editor/Unity.exe'
    $hubEditorPath = Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$version/Editor/Unity.exe"
    if (Test-Path -LiteralPath $hubEditorPath) { $editorPath = $hubEditorPath }

    if (-not (Test-Path -LiteralPath $editorPath)) {
        Write-SetupStatus 'downloading' 'Downloading official Unity Editor; interrupted transfers can resume by running this script again.'
        $downloadUrl = 'https://download.unity3d.com/download_unity/f99f05b3e950/Windows64EditorInstaller/UnitySetup64-6000.0.62f1.exe'
        $expectedBytes = 4086441704L
        if (-not (Test-Path -LiteralPath $installerPath) -or (Get-Item -LiteralPath $installerPath).Length -ne $expectedBytes) {
            & curl.exe --location --fail --silent --show-error --retry 2 --connect-timeout 20 --max-time 21600 --continue-at - --output $installerPath $downloadUrl
            if ($LASTEXITCODE -ne 0) { throw 'Editor download did not complete. The partial file is preserved for resume.' }
        }
        Write-SetupStatus 'verifying' 'Checking installer length, release checksum, and Unity publisher signature.'
        if ((Get-Item -LiteralPath $installerPath).Length -ne $expectedBytes) { throw 'Unexpected installer length.' }
        # Release checksum supplied by the official Unity release API; signature authenticates the publisher.
        if ((Get-FileHash -LiteralPath $installerPath -Algorithm MD5).Hash -ne 'F9720B609AC3B3036E3C4E64567E0041') {
            throw 'Installer checksum does not match the official Unity release.'
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Unity') {
            throw 'Installer publisher signature could not be verified. Installer was not launched.'
        }
        if ($DownloadOnly) { Write-SetupStatus 'downloaded' 'Verified installer is ready.'; exit 0 }
        Write-SetupStatus 'installing' "Installing Unity to $installRoot. Windows may require elevation confirmation."
        # NSIS requires /D last and unquoted, including when its value contains spaces.
        $installer = Start-Process -FilePath $installerPath -ArgumentList "/S /D=$installRoot" -WindowStyle Hidden -PassThru -Wait
        if ($installer.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $editorPath)) {
            throw "Unity installation failed or needs Windows confirmation. Installer exit: $($installer.ExitCode)."
        }
    }

    Write-SetupStatus 'testing-core' 'Running standalone ship simulation tests.'
    & (Join-Path $PSScriptRoot 'Test-Core.ps1')
    $unityCliPath = Join-Path $env:LOCALAPPDATA 'Unity/bin/unity.exe'
    if (Test-Path -LiteralPath $unityCliPath) {
        & $unityCliPath editors add $editorPath --non-interactive --json
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Could not register Editor in Hub; continuing with the verified Editor executable.' }
    }

    Write-SetupStatus 'building' 'Importing project and building Windows. See TestResults/unity-build.log.'
    $buildLogPath = Join-Path $resultsPath 'unity-build.log'
    $editorArguments = '-batchmode -quit -projectPath "' + $projectRoot + '" -executeMethod TradeWinds.Editor.PrototypeSetup.BuildWindows -logFile "' + $buildLogPath + '"'
    $editorProcess = Start-Process -FilePath $editorPath -ArgumentList $editorArguments -WindowStyle Hidden -PassThru
    if (-not $editorProcess.WaitForExit(2700000)) {
        $editorProcess.Kill()
        throw 'Unity build exceeded 45 minutes. Check the build log for package, licensing, or compilation problems.'
    }
    $buildPath = Join-Path $projectRoot 'Builds/Windows/FirstVoyage.exe'
    if ($editorProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $buildPath)) {
        throw 'Unity build did not succeed. Check TestResults/unity-build.log; licensing may require signing in through Hub.'
    }
    Write-SetupStatus 'built' "Windows build created at $buildPath. Visual and interactive testing are still required."
}
catch {
    if ($setupLock -ne $null) { Write-SetupStatus 'failed' $_.Exception.Message }
    Write-Error $_
    exit 1
}
finally { if ($setupLock -ne $null) { $setupLock.Dispose() } }
