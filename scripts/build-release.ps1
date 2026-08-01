[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $projectRoot "dist"
$artifactsRoot = Join-Path $projectRoot "artifacts"

& (Join-Path $PSScriptRoot "fetch-ffmpeg.ps1")
dotnet test (Join-Path $projectRoot "VersaConvert.sln") -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "I test automatici non sono riusciti."
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

$appProject = Join-Path $projectRoot "src\VersaConvert.App\VersaConvert.App.csproj"
dotnet publish $appProject -c $Configuration -r $Runtime --self-contained true -o $distRoot
if ($LASTEXITCODE -ne 0) {
    throw "La pubblicazione .NET non e riuscita."
}

$executable = Join-Path $distRoot "VersaConvert.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "La pubblicazione non ha prodotto VersaConvert.exe."
}

$releaseFiles = @(
    $executable,
    (Join-Path $projectRoot "LICENSE"),
    (Join-Path $projectRoot "THIRD-PARTY-NOTICES.md")
)
$zipPath = Join-Path $artifactsRoot "VersaConvert-win-x64.zip"
Compress-Archive -LiteralPath $releaseFiles -DestinationPath $zipPath -CompressionLevel Optimal -Force

$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
$hashLine = "$($hash.Hash.ToLowerInvariant())  VersaConvert.exe"
$hashPath = Join-Path $artifactsRoot "SHA256SUMS.txt"
Set-Content -LiteralPath $hashPath -Value $hashLine -Encoding ascii

Write-Host "Build completata:"
Write-Host "  EXE:  $executable"
Write-Host "  ZIP:  $zipPath"
Write-Host "  HASH: $hashLine"
