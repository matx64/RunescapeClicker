# RunescapeClicker Native Bootstrap

This directory contains the Phase 1 Windows-native bootstrap for the future rewrite.

## Prerequisites

- Windows 11 x64 with the Windows SDK `10.0.26100.0`
- .NET SDK `10.0.201` or newer in the .NET 10 line
- Visual Studio 2026 with WinUI / Windows App SDK tooling if you want to open and run the app from the IDE
- Developer Mode enabled if unpackaged F5/debug launch fails locally

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

The self-contained unpackaged publish output lands under:

```text
native\src\RunescapeClicker.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\
```

## Notes

- `/old-version` remains the frozen Rust reference and should not be modified during the native migration.
- Phase 1 intentionally ships only a placeholder WinUI shell plus the solution boundaries needed for Phase 2.
