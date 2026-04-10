# Legacy Rust Reference

This directory preserves the original Rust implementation as a migration reference only.

## Status

- Unsupported for current development
- Not the shipping application
- Not the maintained developer path
- Linux support ended with the native rewrite

## Maintained App

The supported product now lives in [native](C:/Users/mathe/Documents/dev/RunescapeClicker/native) and is a Windows 11 x64 WinUI 3 desktop app.

## Why This Folder Still Exists

- It captures the original behavior used to drive the native migration.
- It provides historical Rust source and tests for comparison.
- It should be treated as read-only legacy material unless a migration reference needs to be checked.

## Legacy Notes

- The historical Rust app was cross-platform and included Linux-specific X11 and Wayland behavior.
- Those Linux paths are intentionally not part of the maintained Windows-native application.
- Cargo is no longer part of the default development workflow for this repo.
