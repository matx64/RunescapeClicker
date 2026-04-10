**Windows-Native Rewrite Plan**

**Summary**
- Rewrite the app as a Windows-only native desktop application in C# with WinUI 3, with a clean-break .NET solution and no Linux compatibility work.
- Follow an execution-first migration: extract the automation engine into a testable C# core before any UI rebuild, then rebuild the interface entirely with native WinUI 3 controls.
- Ground truth today: `[src/app.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/src/app.rs)` is a 1,541-line mixed UI/orchestration file, `[src/executor.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/src/executor.rs)` already contains a reusable execution seam, and all current Rust tests passed on April 10, 2026 (`61/61`, including `[tests/executor_integration.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/tests/executor_integration.rs)`).

**Target Architecture And Public Interfaces**
- New solution layout: `/native/RunescapeClicker.sln`, with `RunescapeClicker.Core`, `RunescapeClicker.Automation.Windows`, `RunescapeClicker.App`, `RunescapeClicker.Core.Tests`, and `RunescapeClicker.App.Tests`.
- `RunescapeClicker.Core` owns immutable contracts only: `AutomationAction` base record, `MouseClickAction`, `KeyPressAction`, `DelayAction`, `MouseButtonKind`, `StopCondition`, `RunRequest`, `ExecutionProfile`, `RunEvent`, `RunResult`, and `EngineError`.
- `KeyPressAction` stores canonical Windows key metadata, not free-form text: `VirtualKey`, display label, scan code, and extended-key flag.
- `RunRequest` is the only UI-to-engine input: ordered action list, stop condition, and execution profile.
- `IClickerEngine.ExecuteAsync(RunRequest, IProgress<RunEvent>, CancellationToken)` is the only engine entrypoint.
- `RunescapeClicker.Automation.Windows` implements `IInputAdapter`, `IHotkeyService`, and `ICoordinatePickerService` with Win32 interop; no WinUI types are allowed in this layer.
- `RunescapeClicker.App` is MVVM-only and depends on the abstractions above; all WinUI pages and view models consume immutable snapshots from the core.

**Phase 0: Freeze The Rust App As Reference**
1. Capture the current behavioral contract from `[src/action.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/src/action.rs)`, `[src/executor.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/src/executor.rs)`, `[src/hotkey.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/src/hotkey.rs)`, README, and existing tests.
2. Mark as preserved for v1: three action types, continuous loop execution, stop-by-timer or global stop hotkey, surfaced execution errors, coordinate-based mouse clicks, and action reordering/removal.
3. Mark as intentionally dropped: Linux/Wayland/X11 paths, focused-only hotkey mode, Linux overlay fallback logic, egui-specific custom rendering, and raw text key entry.
4. Write a migration acceptance matrix that maps every Rust executor test to a future C# test and every app workflow to a WinUI replacement.

**Phase 1: Bootstrap The New Windows Solution**
1. Create a fresh `/native` .NET solution targeting `net10.0-windows` with WinUI 3 on the latest stable Windows App SDK as of April 10, 2026: `1.8.6`; do not build v1 on `2.0 Preview 2`.
2. Make the app unpackaged, x64-only, self-contained for release builds, and optimized for currently supported Windows 11 desktop releases only.
3. Add `CommunityToolkit.Mvvm` for state/commands, `Microsoft.Windows.CsWin32` for P/Invoke generation, `xUnit` plus `FluentAssertions` for tests, and a central `Directory.Build.props` for shared warnings/analyzers.
4. Keep the Rust project read-only during development as a reference artifact only; no Rust-to-C# bridge and no mixed-runtime shipping plan.

**Phase 2: Extract And Rebuild The Execution Engine First**
1. Port the executor concepts from Rust into `RunescapeClicker.Core`: cancellable sleep, looping sequence runner, stop conditions, structured failure reporting, and humanized mouse movement.
2. Redesign timing behavior into an explicit `ExecutionProfile` instead of scattered constants so the engine can evolve without UI rewrites.
3. Preserve action types but not Rust internals: keep mouse/key/delay semantics, while allowing a cleaner C# implementation of movement interpolation, jitter policy, and key normalization.
4. Build fake `IInputAdapter` and fake time/random providers so the engine is fully testable without real mouse or keyboard injection.
5. Port all executor coverage from `[tests/executor_integration.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/tests/executor_integration.rs)` to C#: backend failure, stop during delay, stop during movement, click failure after move, same-position click delay, empty sequence, and bounded mouse interpolation.
6. Exit criterion: the C# core passes the ported contract suite before any WinUI screen is treated as feature-complete.

**Phase 3: Implement Windows-Native Automation Services**
1. Build `WindowsInputAdapter` on `SendInput` and `GetCursorPos`, with explicit handling for zero-event injection, blocked input, and integrity-level/UIPI failures.
2. Build `GlobalHotkeyService` on `RegisterHotKey` plus `WM_HOTKEY`, using fixed defaults of `F1` for cursor capture and `F2` for stop, with `MOD_NOREPEAT` and deterministic unregister on shutdown.
3. Build `CoordinatePickerService` as a dedicated topmost borderless overlay window that darkens the screen, shows a crosshair, captures click coordinates, and cancels on `Esc`.
4. Route all HWND/message-loop work through a single interop boundary so WinUI view models never process raw window messages.
5. Add manual smoke harnesses for real hotkeys and real input injection, because those behaviors should not run in CI.

**Phase 4: Build The Application Layer And State Model**
1. Create `MainViewModel`, `ActionComposerViewModel`, `ActionListViewModel`, `RunPanelViewModel`, and `StatusViewModel`, backed by a single in-memory session store.
2. Model explicit app states: `Idle`, `CapturingCoordinate`, `EditingAction`, `ReadyToRun`, `Running`, `Stopping`, and `Faulted`.
3. Use commands for `AddMouseClick`, `AddKeyPress`, `AddDelay`, `EditAction`, `DeleteAction`, `MoveAction`, `CaptureCoordinate`, `StartRun`, and `StopRun`.
4. Build a run coordinator that converts mutable UI state into immutable `RunRequest` snapshots, starts the engine on a background task, and marshals progress/error events back to the UI thread.
5. Keep v1 session-only: no save/load profiles, no import/export, and no background service mode.

**Phase 5: Rebuild The UI From Scratch In WinUI 3**
1. Replace the egui single-surface UI with a WinUI 3 single-window shell using stock controls only: `CommandBar`, `ComboBox`, `RadioButtons`, `NumberBox`, `AutoSuggestBox` or captured key field, `ListView`, `InfoBar`, `ContentDialog`, `ToolTip`, `TeachingTip`, and `ProgressRing`.
2. Use a two-pane adaptive layout: left pane for action composition and editing, right pane for ordered sequence, stop rule configuration, and run controls; collapse into a stacked mobile-style layout below the chosen width breakpoint.
3. Mouse click flow: choose left/right button, capture coordinate via `F1` or “Pick on screen,” preview the selected point, then confirm the action.
4. Key press flow: capture a real key from the keyboard and store normalized Windows key metadata; do not use raw lowercase string entry in the new UI.
5. Sequence management: `ListView` with drag-and-drop reorder, inline action summaries, edit/remove affordances, and disabled editing while a run is active.
6. Run experience: large native Start/Stop controls, stop rule selector, timer `NumberBox`, live status `InfoBar`, and clear hotkey hints.
7. Visual direction: native Windows styling first, Segoe Fluent icons or `SymbolIcon`, system theme/high-contrast support, high-DPI correctness, and no custom-painted icon system in v1.

**Phase 6: Hardening, Packaging, And Cutover**
1. Add a Windows-only CI pipeline because the repo currently has no existing CI: restore, build, test, and publish the WinUI app on Windows runners.
2. Produce self-contained unpackaged x64 artifacts first, then add an installer/bootstrapper that checks the Windows App Runtime prerequisite and provides a guided install path.
3. Add friendly runtime messaging for unsupported cases: hotkey collision, blocked input injection, elevated target window, and coordinate-picker cancellation.
4. Update all docs to say Linux support ended and Windows is the only supported platform.
5. When acceptance passes, archive the Rust app as legacy reference, remove Cargo from the default developer path, and make the .NET solution the sole maintained application.

**Test Plan**
- Port every Rust executor scenario into C# unit/integration tests before feature sign-off.
- Add view-model tests for add/edit/remove/reorder, invalid timer or delay input, state transitions, and Start/Stop button enablement.
- Add Windows integration tests for hotkey registration lifecycle, picker confirm/cancel, and progress/error propagation with mocked interop.
- Run manual smoke passes on multi-monitor setups, 100/150/200% DPI, hotkey conflict scenarios, and elevated-target failure messaging.
- Acceptance for cutover: create each action type, reorder actions, capture coordinates with `F1` and overlay click, run continuously, stop via `F2`, stop via timer, and recover cleanly from injection errors.

**Assumptions And Defaults**
- As of April 10, 2026, the plan uses `.NET 10 LTS` and stable `Windows App SDK 1.8.6`; preview SDKs are out of scope for v1.
- Supported platform for the shipped app is Windows 11 desktop only, x64 only, unpackaged only for the first release.
- v1 remains single-session and in-memory; persistence, profiles, and import/export are explicitly deferred.
- The app runs unelevated by default; if Windows blocks automation into a higher-integrity target, the app shows a clear remediation message instead of implementing UIAccess or privileged injection.
- No Linux code, no cross-platform abstractions beyond testable engine boundaries, and no attempt to preserve Wayland/X11 behaviors.

**References**
- Rust reference points: `[src/app.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/src/app.rs)`, `[src/executor.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/src/executor.rs)`, `[tests/executor_integration.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/tests/executor_integration.rs)`.
- Windows App SDK release policy: [Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels).
- WinUI 3 desktop interop guidance: [Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/desktop-winui3-app-with-basic-interop).
- .NET supported releases: [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).
- Win32 APIs for implementation: [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) and [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).
