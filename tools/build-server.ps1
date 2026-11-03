<#
.SYNOPSIS
  Builds the Linux Dedicated Server headless and packs it as unity/Builds/server-<version>.zip,
  ready to upload to S3 as builds/server-<version>.zip.

.DESCRIPTION
  Runs Unity in batch mode with MM.EditorTools.BuildScript.BuildLinuxServer, then zips
  unity/Builds/LinuxServer (without the *_DoNotShip debug folder). <version> comes from
  build-info.txt, i.e. Player Settings > Version: bump it for every build you upload.

  Close the Unity Editor first: batch mode cannot open a project that is already open.

.EXAMPLE
  .\tools\build-server.ps1
  .\tools\build-server.ps1 -UnityPath "D:\Unity\6000.3.10f1\Editor\Unity.exe"
#>
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot "unity"
$buildDir = Join-Path $project "Builds\LinuxServer"
$logFile  = Join-Path $project "Builds\build-server.log"

if (-not (Test-Path $UnityPath)) {
    throw "Unity not found at '$UnityPath'. Pass -UnityPath with the 6000.3.10f1 Unity.exe."
}

$openEditor = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -like "*$project*" -and $_.CommandLine -notlike "*AssetImportWorker*" }
if ($openEditor) {
    throw "Unity is open on $project. Close it first (batch mode needs the project unlocked)."
}

New-Item -ItemType Directory -Force -Path (Split-Path $logFile) | Out-Null

Write-Host "Building Linux Dedicated Server (log: $logFile)..."
$unityArgs = @(
    "-batchmode", "-quit", "-nographics",
    "-projectPath", "`"$project`"",
    "-executeMethod", "MM.EditorTools.BuildScript.BuildLinuxServer",
    "-logFile", "`"$logFile`""
)
# Unity.exe is a GUI app: without -Wait PowerShell would return before the build ends.
$unity = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
if ($unity.ExitCode -ne 0) {
    Get-Content $logFile -Tail 40
    throw "Unity build failed (exit code $($unity.ExitCode)). Full log: $logFile"
}

$infoPath = Join-Path $buildDir "build-info.txt"
if (-not (Test-Path $infoPath)) {
    throw "Build reported success but $infoPath is missing."
}
$info = Get-Content $infoPath -Raw | ConvertFrom-StringData
$version = $info.version
$zipPath = Join-Path $project "Builds\server-$version.zip"

# Windows' bsdtar writes zip entries with forward slashes (Compress-Archive in PowerShell 5.1 writes
# backslashes, which break on Linux). Neither keeps the Unix execute bit: chmod +x after unzipping.
$items = Get-ChildItem $buildDir | Where-Object { $_.Name -notlike "*_DoNotShip" } | ForEach-Object { $_.Name }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
& "$env:SystemRoot\System32\tar.exe" -a -c -f $zipPath -C $buildDir @items
if ($LASTEXITCODE -ne 0) { throw "Creating $zipPath failed." }

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "OK  $zipPath ($sizeMb MB)  commit=$($info.commit) dirty=$($info.dirty)"
Write-Host "Upload (S3 bucket of the AWS side):  aws s3 cp `"$zipPath`" s3://<bucket>/builds/server-$version.zip"
Write-Host "On Linux after unzip:                chmod +x Server.x86_64"
