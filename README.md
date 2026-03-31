# Runescape Clicker

A cross-platform Runescape Clicker with graphical interface built in Rust.

## Features

- Left or Right Click on desired position (F1 to capture mouse coordinates)
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
- Linux: `libxdo-dev` (`sudo apt install libxdo-dev`)

### Build & Run

```bash
cargo run --release
```

## Usage

1. Add actions using the toolbar buttons (Mouse Click, Keyboard Press, Delay)
2. Configure the stop condition (F2 only, or timer + F2)
3. Press START to begin the automation loop
4. Press F2 or click STOP to halt execution
