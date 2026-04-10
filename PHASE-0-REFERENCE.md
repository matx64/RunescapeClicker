# Phase 0 Reference Baseline

This document freezes the Rust legacy app as the migration reference for the native Windows rewrite described in [NATIVE-PLAN.md](C:/Users/mathe/Documents/dev/RunescapeClicker/NATIVE-PLAN.md). The companion implementation checklist lives in [PHASE-0-ACCEPTANCE-MATRIX.md](C:/Users/mathe/Documents/dev/RunescapeClicker/PHASE-0-ACCEPTANCE-MATRIX.md).

## Canonical Legacy Source

- Phase 0 treats [old-version](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version) as the canonical frozen Rust reference.
- The deleted root Rust layout is not restored. All code references for the migration baseline point at `old-version/...`.
- When README and code disagree, code and tests win.

## Verification Snapshot

- Verified on April 10, 2026 by running `cargo test` in [old-version](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version).
- Result: `61/61` tests passed.
- Coverage source for this baseline comes from:
  - [README.md](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/README.md)
  - [action.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/action.rs)
  - [executor.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/executor.rs)
  - [hotkey.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/hotkey.rs)
  - [app.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/app.rs)
  - [executor_integration.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/tests/executor_integration.rs)

## Frozen Public Contracts

- `Action`
  - `MouseClick { button, x, y }`
  - `KeyPress { key }`
  - `Delay { ms }`
- `MouseButton`
  - `Left`
  - `Right`
- `StopCondition`
  - `HotkeyOnly`
  - `Timer { seconds }`
- Executor seams frozen for the port:
  - input driver abstraction
  - runtime abstraction
  - looping sequence execution
  - cooperative stop handling
  - surfaced fatal status/error reporting
- Hotkey and capture seams frozen for the port:
  - global stop hotkey
  - direct `F1` coordinate capture
  - picker-based coordinate capture

## Preserved For V1

- Three action types: mouse click, key press, and delay.
- Continuous looping execution until stopped.
- Stop by timer or stop hotkey.
- Surfaced execution failures through status/error messages.
- Coordinate-based mouse clicks with left/right button selection.
- Action removal and reordering.
- Anti-detect micro-delays before input actions.
- Human-like mouse interpolation, including bounded drift and post-move click delay.

## Intentionally Dropped

- Linux, Wayland, and X11 backend selection behavior.
- Focused-only hotkey mode used on Wayland.
- egui-specific layout, rendering, viewport manipulation, and icon presentation details.
- Overlay fallback logic as a Linux compatibility feature.
- Raw free-text key entry as the long-term Windows-native input model.

## Behavioral Notes To Preserve

- Invalid delay input is rejected and leaves the delay form open.
- Invalid timer input keeps the previous stop condition unchanged.
- Clearing actions removes only the action list and preserves editor state.
- Latest status message wins when multiple messages arrive.
- Clicking a coordinate that matches the current cursor position still waits before clicking.
- If the current mouse location cannot be read, mouse-click execution falls back to one absolute move followed by click.
- Cancelling the coordinate picker keeps the mouse-click form open and preserves the previously selected coordinates.
- “Add Mouse Click” opens the picker first even when direct capture is available.
- Direct `F1` capture only applies while the mouse-click form is open and no picker is active.

## Documentation Clarifications

- The legacy README describes the app as cross-platform, but the migration baseline is now Windows-only.
- The legacy README summarizes usage correctly at a high level, but several behaviors are only fully defined by code and tests.
- The native rewrite should preserve behavior-level contracts from the Rust app, not egui implementation details.

## Source-Of-Truth Notes

- README feature list: [README.md](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/README.md)
- Action formatting and stop-condition types: [action.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/action.rs)
- Execution timing, stop logic, mouse movement, and key normalization: [executor.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/executor.rs)
- Input backend selection, hotkey support modes, and coordinate capture: [hotkey.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/hotkey.rs)
- User workflow and editor-state behavior: [app.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/app.rs)
- Porting checklist and C# replacement mapping: [PHASE-0-ACCEPTANCE-MATRIX.md](C:/Users/mathe/Documents/dev/RunescapeClicker/PHASE-0-ACCEPTANCE-MATRIX.md)
