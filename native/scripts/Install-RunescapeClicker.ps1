[CmdletBinding()]
param(
    [string]$InstallPath = "",
    [switch]$LaunchAfterInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$installMarkerFileName = ".runescape-clicker-install"

function Assert-WindowsHost {
    if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows))
    {
        throw "Runescape Clicker can only be installed on Windows."
    }
}

function Assert-Windows11X64 {
    if (-not [Environment]::Is64BitOperatingSystem)
    {
        throw "Runescape Clicker requires a 64-bit version of Windows."
    }

    $version = [Environment]::OSVersion.Version
    if ($version.Build -lt 26100)
    {
        throw "Runescape Clicker requires Windows 11 with build 26100 or newer."
    }
}

function Resolve-InstallPath([string]$requestedPath) {
    if ([string]::IsNullOrWhiteSpace($requestedPath))
    {
        return [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "RunescapeClicker"))
    }

    if (-not [System.IO.Path]::IsPathRooted($requestedPath))
    {
        throw "InstallPath must be an absolute path."
    }

    return [System.IO.Path]::GetFullPath($requestedPath)
}

function Assert-SafeInstallPath([string]$resolvedPath) {
    $root = [System.IO.Path]::GetPathRoot($resolvedPath)
    if ([string]::Equals($resolvedPath.TrimEnd("\"), $root.TrimEnd("\"), [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to install into the drive root."
    }
}

function Assert-InstallDirectoryReady([string]$resolvedPath) {
    if (-not (Test-Path -LiteralPath $resolvedPath))
    {
        return
    }

    $entries = @(Get-ChildItem -LiteralPath $resolvedPath -Force)
    if ($entries.Count -eq 0)
    {
        return
    }

    $markerPath = Join-Path $resolvedPath $installMarkerFileName
    if (Test-Path -LiteralPath $markerPath)
    {
        return
    }

    throw "InstallPath must point to an empty directory or an existing Runescape Clicker install."
}

function Clear-InstallDirectory([string]$resolvedPath) {
    if (-not (Test-Path -LiteralPath $resolvedPath))
    {
        New-Item -ItemType Directory -Path $resolvedPath -Force | Out-Null
        return
    }

    Get-ChildItem -LiteralPath $resolvedPath -Force | Remove-Item -Recurse -Force
}

function Copy-InstallPayload([string]$sourceRoot, [string]$resolvedPath) {
    Get-ChildItem -LiteralPath $sourceRoot -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $resolvedPath -Recurse -Force
    }
}

function New-StartMenuShortcut([string]$targetPath, [string]$workingDirectory) {
    $programsDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    $shortcutPath = Join-Path $programsDir "Runescape Clicker.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $targetPath
    $shortcut.WorkingDirectory = $workingDirectory
    $shortcut.IconLocation = "$targetPath,0"
    $shortcut.Save()
}

Assert-WindowsHost
Assert-Windows11X64

$sourceRoot = Join-Path $PSScriptRoot "app"
$sourceExe = Join-Path $sourceRoot "RunescapeClicker.App.exe"
if (-not (Test-Path -LiteralPath $sourceExe))
{
    throw "RunescapeClicker.App.exe was not found next to the installer. Extract the full package before running this script."
}

$resolvedInstallPath = Resolve-InstallPath $InstallPath
Assert-SafeInstallPath $resolvedInstallPath
Assert-InstallDirectoryReady $resolvedInstallPath
Clear-InstallDirectory $resolvedInstallPath

Copy-InstallPayload $sourceRoot $resolvedInstallPath
Set-Content -LiteralPath (Join-Path $resolvedInstallPath $installMarkerFileName) -Value "Runescape Clicker install marker" -Encoding ascii

$installedExe = Join-Path $resolvedInstallPath "RunescapeClicker.App.exe"
New-StartMenuShortcut -targetPath $installedExe -workingDirectory $resolvedInstallPath

Write-Host "Runescape Clicker installed to $resolvedInstallPath"
Write-Host "A Start Menu shortcut named 'Runescape Clicker' was created for the current user."

if ($LaunchAfterInstall)
{
    Start-Process -FilePath $installedExe -WorkingDirectory $resolvedInstallPath
}
