# Runescape Clicker

Runescape Clicker is now maintained as a Windows-native desktop app built with C#, WinUI 3, and Win32 automation services.

The supported product lives in [native](C:/Users/mathe/Documents/dev/RunescapeClicker/native). The Rust app in [old-version](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version) remains in the repo only as a legacy migration reference and is not the supported shipping application.

## Supported Platform

- Windows 11 x64 only
- Unpackaged self-contained release builds
- `.NET SDK 10.0.201` for local development, pinned by [global.json](C:/Users/mathe/Documents/dev/RunescapeClicker/global.json)

## Build

```powershell
dotnet build .\native\RunescapeClicker.sln -c Debug -p:Platform=x64
```

## Test

```powershell
dotnet test .\native\RunescapeClicker.sln -c Debug -p:Platform=x64
```

## Package

```powershell
pwsh .\native\scripts\Create-Package.ps1
```

The package script publishes the WinUI app, stages a self-contained installer layout under `native\artifacts\RunescapeClicker-win-x64\`, and creates `native\artifacts\RunescapeClicker-win-x64.zip`.

## Install

After extracting the package, run:

```powershell
pwsh .\Install-RunescapeClicker.ps1
```

The installer copies the app into `%LocalAppData%\RunescapeClicker` and creates a Start Menu shortcut for the current user.

## Manual Validation

- Launch the installed app on Windows 11 x64.
- Confirm `F1` captures coordinates while a mouse draft is open.
- Confirm `F2` stops an active run.
- Confirm picker cancellation leaves the current draft coordinate unchanged.
- Confirm friendly messaging appears when hotkeys collide or Windows blocks input into an elevated target.
