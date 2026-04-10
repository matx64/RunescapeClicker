# Runescape Clicker Native App

This directory contains the maintained Windows-native Runescape Clicker solution.

## Requirements

- Windows 11 x64, build `26100` or newer
- `.NET SDK 10.0.201`, pinned in [global.json](C:/Users/mathe/Documents/dev/RunescapeClicker/global.json)
- Visual Studio 2026 with WinUI / Windows App SDK tooling if you want IDE debugging
- Developer Mode can still help with unpackaged local F5/debug launch

## Solution Layout

- `src/RunescapeClicker.Core`: immutable contracts and the automation engine
- `src/RunescapeClicker.Automation.Windows`: Win32 input, hotkey, and picker services
- `src/RunescapeClicker.App`: WinUI 3 shell and MVVM app layer
- `tests/*`: unit and integration coverage for core, Windows automation, and app behavior
- `scripts/`: packaging and install tooling for self-contained releases

## Build

```powershell
dotnet build .\native\RunescapeClicker.sln -c Debug -p:Platform=x64
```

## Test

```powershell
dotnet test .\native\RunescapeClicker.sln -c Debug -p:Platform=x64
```

## Publish

```powershell
dotnet publish .\native\src\RunescapeClicker.App\RunescapeClicker.App.csproj -c Release -p:Platform=x64 -r win-x64 -p:PublishProfile=win-x64
```

The raw self-contained publish output lands under:

```text
native\src\RunescapeClicker.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\
```

## Create A Release Package

```powershell
pwsh .\native\scripts\Create-Package.ps1
```

This produces:

- `native\artifacts\RunescapeClicker-win-x64\`
- `native\artifacts\RunescapeClicker-win-x64.zip`

The package includes a self-contained `app\` folder, `Install-RunescapeClicker.ps1`, and a small package README.

## Install From A Package

After extracting the generated package:

```powershell
pwsh .\Install-RunescapeClicker.ps1
```

By default the installer copies the app to `%LocalAppData%\RunescapeClicker` and creates a Start Menu shortcut for the current user.

## Runtime Notes

- Global hotkeys are fixed to `F1` for coordinate capture and `F2` for run stop.
- If another app already owns `F1` or `F2`, the app surfaces a friendly collision message and keeps `Retry Hotkeys` available.
- If Windows blocks automated input into an elevated target app, run both apps at the same privilege level.

## Manual Smoke Checklist

- Launch the app locally and from the packaged install.
- Confirm `F1` capture, overlay picking, and picker cancellation behavior.
- Confirm `F2` stop during an active run.
- Confirm blocked-input messaging against an elevated target window.
- Confirm the generated zip installs cleanly on a separate Windows 11 x64 machine.
