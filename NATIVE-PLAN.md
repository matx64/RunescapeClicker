**Windows-Native Rewrite Plan**

**Summary**
- Rewrite the app as a Windows-only native desktop application in C# with WinUI 3, with a clean-break .NET solution and no Linux compatibility work.
- Follow an execution-first migration: extract the automation engine into a testable C# core before any UI rebuild, then rebuild the interface entirely with native WinUI 3 controls.
- Ground truth today: `[old-version/src/app.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/app.rs)` is a 1,541-line mixed UI/orchestration file, `[old-version/src/executor.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/executor.rs)` already contains a reusable execution seam, and all current Rust tests passed on April 10, 2026 (`61/61`, including `[old-version/tests/executor_integration.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/tests/executor_integration.rs)`).
- Current migration status: Phases 0, 1, and 2 are complete as of April 10, 2026. The repo now contains a validated native bootstrap and execution core at `[native/RunescapeClicker.sln](C:/Users/mathe/Documents/dev/RunescapeClicker/native/RunescapeClicker.sln)` while `[old-version](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version)` remains the frozen Rust reference.

**Target Architecture And Public Interfaces**
- New solution layout: `/native/RunescapeClicker.sln`, with `RunescapeClicker.Core`, `RunescapeClicker.Automation.Windows`, `RunescapeClicker.App`, `RunescapeClicker.Core.Tests`, and `RunescapeClicker.App.Tests`.
- `RunescapeClicker.Core` owns immutable contracts only: `AutomationAction` base record, `MouseClickAction`, `KeyPressAction`, `DelayAction`, `MouseButtonKind`, `StopCondition`, `RunRequest`, `ExecutionProfile`, `RunEvent`, `RunResult`, and `EngineError`.
- `KeyPressAction` stores canonical Windows key metadata, not free-form text: `VirtualKey`, display label, scan code, and extended-key flag.
- `RunRequest` is the only UI-to-engine input: ordered action list, stop condition, and execution profile.
- `IClickerEngine.ExecuteAsync(RunRequest, IProgress<RunEvent>, CancellationToken)` is the only engine entrypoint.
- `RunescapeClicker.Automation.Windows` implements `IInputAdapter`, `IHotkeyService`, and `ICoordinatePickerService` with Win32 interop; no WinUI types are allowed in this layer.
- `RunescapeClicker.App` is MVVM-only and depends on the abstractions above; all WinUI pages and view models consume immutable snapshots from the core.

**Execution Status**
- Phase 0: Completed on April 10, 2026.
- Phase 1: Completed on April 10, 2026.
- Phase 2: Completed on April 10, 2026.
- Phase 3: Pending.
- Phase 4: Pending.
- Phase 5: Pending.
- Phase 6: Pending.

**Phase 0: Freeze The Rust App As Reference (Completed April 10, 2026)**
1. [x] Capture the current behavioral contract from `[old-version/src/action.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/action.rs)`, `[old-version/src/executor.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/executor.rs)`, `[old-version/src/hotkey.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/hotkey.rs)`, README, and existing tests.
2. [x] Mark as preserved for v1: three action types, continuous loop execution, stop-by-timer or global stop hotkey, surfaced execution errors, coordinate-based mouse clicks, and action reordering/removal.
3. [x] Mark as intentionally dropped: Linux/Wayland/X11 paths, focused-only hotkey mode, Linux overlay fallback logic, egui-specific custom rendering, and raw text key entry.
4. [x] Write a migration acceptance matrix that maps every Rust executor test to a future C# test and every app workflow to a WinUI replacement.

**Phase 0 Completion Notes**
- The frozen migration baseline is documented in `[PHASE-0-REFERENCE.md](C:/Users/mathe/Documents/dev/RunescapeClicker/PHASE-0-REFERENCE.md)`.
- The Rust-to-C# contract mapping is documented in `[PHASE-0-ACCEPTANCE-MATRIX.md](C:/Users/mathe/Documents/dev/RunescapeClicker/PHASE-0-ACCEPTANCE-MATRIX.md)`.
- The Rust app remains read-only in `[old-version](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version)`.
- `cargo test` in `[old-version](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version)` passed on April 10, 2026 with `61/61` tests green.

**Phase 1: Bootstrap The New Windows Solution (Completed April 10, 2026)**
1. [x] Create a fresh `/native` .NET solution targeting `net10.0-windows` with WinUI 3 on the latest stable Windows App SDK as of April 10, 2026: `1.8.6`; do not build v1 on `2.0 Preview 2`.
2. [x] Make the app unpackaged, x64-only, self-contained for release builds, and optimized for currently supported Windows 11 desktop releases only.
3. [x] Add `CommunityToolkit.Mvvm` for state/commands, `Microsoft.Windows.CsWin32` for P/Invoke generation, `xUnit` plus `FluentAssertions` for tests, and a central `Directory.Build.props` for shared warnings/analyzers.
4. [x] Keep the Rust project read-only during development as a reference artifact only; no Rust-to-C# bridge and no mixed-runtime shipping plan.

**Phase 1 Completion Notes**
- The native bootstrap solution now exists at `[native/RunescapeClicker.sln](C:/Users/mathe/Documents/dev/RunescapeClicker/native/RunescapeClicker.sln)`.
- Implemented projects:
  - `[native/src/RunescapeClicker.Core](C:/Users/mathe/Documents/dev/RunescapeClicker/native/src/RunescapeClicker.Core)`
  - `[native/src/RunescapeClicker.Automation.Windows](C:/Users/mathe/Documents/dev/RunescapeClicker/native/src/RunescapeClicker.Automation.Windows)`
  - `[native/src/RunescapeClicker.App](C:/Users/mathe/Documents/dev/RunescapeClicker/native/src/RunescapeClicker.App)`
  - `[native/tests/RunescapeClicker.Core.Tests](C:/Users/mathe/Documents/dev/RunescapeClicker/native/tests/RunescapeClicker.Core.Tests)`
  - `[native/tests/RunescapeClicker.App.Tests](C:/Users/mathe/Documents/dev/RunescapeClicker/native/tests/RunescapeClicker.App.Tests)`
- Shared build/analyzer defaults now live in `[native/Directory.Build.props](C:/Users/mathe/Documents/dev/RunescapeClicker/native/Directory.Build.props)`.
- The unpackaged WinUI app shell and release publish profile now live in `[native/src/RunescapeClicker.App/RunescapeClicker.App.csproj](C:/Users/mathe/Documents/dev/RunescapeClicker/native/src/RunescapeClicker.App/RunescapeClicker.App.csproj)` and `[native/src/RunescapeClicker.App/Properties/PublishProfiles/win-x64.pubxml](C:/Users/mathe/Documents/dev/RunescapeClicker/native/src/RunescapeClicker.App/Properties/PublishProfiles/win-x64.pubxml)`.
- Native bootstrap instructions now live in `[native/README.md](C:/Users/mathe/Documents/dev/RunescapeClicker/native/README.md)`.
- Validation completed successfully on April 10, 2026:
  - `dotnet build native/RunescapeClicker.sln -c Debug -p:Platform=x64`
  - `dotnet test native/RunescapeClicker.sln -c Debug -p:Platform=x64`
  - `dotnet publish native/src/RunescapeClicker.App/RunescapeClicker.App.csproj -c Release -p:Platform=x64 -r win-x64 -p:PublishProfile=win-x64`

**Phase 2: Extract And Rebuild The Execution Engine First (Completed April 10, 2026)**
1. [x] Port the executor concepts from Rust into `RunescapeClicker.Core`: cancellable sleep, looping sequence runner, stop conditions, structured failure reporting, and humanized mouse movement.
2. [x] Redesign timing behavior into an explicit `ExecutionProfile` instead of scattered constants so the engine can evolve without UI rewrites.
3. [x] Preserve action types but not Rust internals: keep mouse/key/delay semantics, while allowing a cleaner C# implementation of movement interpolation, jitter policy, and key normalization.
4. [x] Build fake `IInputAdapter` and fake time/random providers so the engine is fully testable without real mouse or keyboard injection.
5. [x] Port all executor coverage from `[old-version/tests/executor_integration.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/tests/executor_integration.rs)` to C#: backend failure, stop during delay, stop during movement, click failure after move, same-position click delay, empty sequence, and bounded mouse interpolation.
6. [x] Exit criterion: the C# core passes the ported contract suite before any WinUI screen is treated as feature-complete.

**Phase 2 Completion Notes**
- `RunescapeClicker.Core` now exposes immutable engine contracts for `AutomationAction`, `MouseClickAction`, `KeyPressAction`, `DelayAction`, `StopCondition`, `RunRequest`, `ExecutionProfile`, `RunEvent`, `RunResult`, `RunOutcome`, `EngineError`, `EngineErrorCode`, `ScreenPoint`, `IClickerEngine`, and `IInputAdapter`.
- The Phase 2 engine now runs the looping action sequence asynchronously through `IClickerEngine.ExecuteAsync(RunRequest, IProgress<RunEvent>, CancellationToken)` with typed outcomes, structured engine faults, cancellable delay handling, and deterministic test seams for fake input and runtime control.
- The C# executor port preserves the Rust behavior contract for anti-detect delays, delay jitter, timer stopping, stop-during-delay, stop-during-movement, post-move click delay, same-position click delay, bounded movement interpolation, and direct move fallback when cursor position cannot be read.
- Key input is now represented as canonical Windows metadata in `KeyPressAction` instead of free-form text while keeping the future Windows automation layer platform-neutral in core.
- Validation completed successfully on April 10, 2026:
  - `dotnet test native/RunescapeClicker.sln -c Debug -p:Platform=x64`

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
- Rust reference points: `[old-version/src/app.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/app.rs)`, `[old-version/src/executor.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/src/executor.rs)`, `[old-version/tests/executor_integration.rs](C:/Users/mathe/Documents/dev/RunescapeClicker/old-version/tests/executor_integration.rs)`.
- Windows App SDK release policy: [Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels).
- WinUI 3 desktop interop guidance: [Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/desktop-winui3-app-with-basic-interop).
- .NET supported releases: [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).
- Win32 APIs for implementation: [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) and [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).
