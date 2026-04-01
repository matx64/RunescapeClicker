# Runescape Clicker

A cross-platform Runescape Clicker with graphical interface built in Rust.

## Features

- Left or Right Click on desired position
- Keyboard Press (any key)
- Delay with millisecond precision
- Automatic stop after N seconds or F2 hotkey
- Built-in random micro-delays for anti-detection

## Tech

- Rust
- egui/eframe (GUI)
- enigo (input simulation)
- global-hotkey (F2 stop, F1 position capture)

## Build from Source

### Prerequisites

- [Rust toolchain](https://rustup.rs/)

### Build & Run

```bash
cargo run --release
```

## Usage

1. Add actions using the toolbar buttons (Mouse Click, Keyboard Press, Delay)
2. Configure the stop condition (F2 only, or timer + F2)
3. Press START to begin the automation loop
4. Press F2 or click STOP to halt execution

## Linux Hotkeys

- X11 sessions: global `F1` capture and global `F2` stop are supported.
- Wayland sessions: `F2` stop works while the app window is focused.
- Wayland sessions: `F1` or `Pick On Screen` opens a transparent overlay on the app window's monitor so you can click the target point to capture `X/Y`.

## Wayland Notes

- This build enables `enigo`'s experimental Wayland backend.
- On Wayland, the app prefers native Wayland injection first and falls back to X11/XWayland when `DISPLAY` is available.
- Move the app window onto the same monitor as the game before using `Pick On Screen`.
- Mouse/keyboard injection still depends on compositor support for the relevant virtual-input protocol.
