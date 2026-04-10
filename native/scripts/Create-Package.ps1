[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ArtifactRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$nativeRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($ArtifactRoot))
{
    $ArtifactRoot = Join-Path $nativeRoot "artifacts"
}
else
{
    $ArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
}

$projectPath = Join-Path $nativeRoot "src\RunescapeClicker.App\RunescapeClicker.App.csproj"
$packageRoot = Join-Path $ArtifactRoot "RunescapeClicker-win-x64"
$appRoot = Join-Path $packageRoot "app"
$zipPath = Join-Path $ArtifactRoot "RunescapeClicker-win-x64.zip"
$installScriptPath = Join-Path $scriptRoot "Install-RunescapeClicker.ps1"
$packageReadmePath = Join-Path $scriptRoot "PACKAGE-README.txt"
$publishedExePath = Join-Path $appRoot "RunescapeClicker.App.exe"

Write-Host "Creating self-contained package under $ArtifactRoot"

if (Test-Path -LiteralPath $packageRoot)
{
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath)
{
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $appRoot -Force | Out-Null

& dotnet publish $projectPath `
    -c $Configuration `
    -p:Platform=x64 `
    -r $RuntimeIdentifier `
    -p:PublishProfile=win-x64 `
    -p:PublishDir="$appRoot\" | Out-Host

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $publishedExePath))
{
    throw "dotnet publish did not produce RunescapeClicker.App.exe."
}

Copy-Item -LiteralPath $installScriptPath -Destination (Join-Path $packageRoot "Install-RunescapeClicker.ps1")
Copy-Item -LiteralPath $packageReadmePath -Destination (Join-Path $packageRoot "README.txt")

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force

Write-Host "Package folder: $packageRoot"
Write-Host "Zip archive: $zipPath"
