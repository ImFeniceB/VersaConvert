[CmdletBinding()]
param(
    [string]$Destination,
    [string]$DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
    [switch]$ForceDownload
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $projectRoot "vendor\ffmpeg.exe"
}
$Destination = [System.IO.Path]::GetFullPath($Destination)
$destinationDirectory = Split-Path -Parent $Destination
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

if ((Test-Path -LiteralPath $Destination -PathType Leaf) -and (Get-Item -LiteralPath $Destination).Length -gt 1MB) {
    Write-Host "FFmpeg e gia disponibile: $Destination"
    exit 0
}

if (-not $ForceDownload) {
    $localCommand = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($null -ne $localCommand) {
        $sourceItem = Get-Item -LiteralPath $localCommand.Source
        $sourcePath = $sourceItem.FullName
        if ($sourceItem.LinkType -and $sourceItem.Target.Count -gt 0) {
            $sourcePath = [System.IO.Path]::GetFullPath([string]$sourceItem.Target[0])
        }
        if ((Test-Path -LiteralPath $sourcePath -PathType Leaf) -and (Get-Item -LiteralPath $sourcePath).Length -gt 1MB) {
            Copy-Item -LiteralPath $sourcePath -Destination $Destination -Force
            Write-Host "FFmpeg copiato dall'installazione locale."
            & $Destination -version | Select-Object -First 1
            exit 0
        }
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("versaconvert-ffmpeg-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $archivePath = Join-Path $temporaryRoot "ffmpeg.zip"
    $extractPath = Join-Path $temporaryRoot "extract"
    Write-Host "Download di FFmpeg da $DownloadUrl"
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath
    $downloadedBinary = Get-ChildItem -LiteralPath $extractPath -Recurse -Filter "ffmpeg.exe" -File | Select-Object -First 1
    if ($null -eq $downloadedBinary) {
        throw "L'archivio scaricato non contiene ffmpeg.exe."
    }
    Copy-Item -LiteralPath $downloadedBinary.FullName -Destination $Destination -Force
    & $Destination -version | Select-Object -First 1
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath($temporaryRoot)
    $systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTemp.StartsWith($systemTemp, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemp)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
