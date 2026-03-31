use crate::action::{Action, MouseButton, StopCondition};
use crate::executor;
use crate::hotkey::{capture_mouse_position, HotkeyManager, HotkeySupport, MouseCaptureSupport};
use eframe::egui;
use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::mpsc::{self, Receiver, Sender};
use std::sync::Arc;
use std::thread::JoinHandle;
use std::time::Duration;

const COLOR_MOUSE: egui::Color32 = egui::Color32::from_rgb(55, 114, 255);
const COLOR_KEYBOARD: egui::Color32 = egui::Color32::from_rgb(179, 0, 137);
const COLOR_DELAY: egui::Color32 = egui::Color32::from_rgb(242, 187, 5);
const COLOR_TOOLBAR_FILL: egui::Color32 = egui::Color32::from_rgb(36, 41, 48);
const COLOR_START: egui::Color32 = egui::Color32::from_rgb(0, 133, 72);
const COLOR_STOP: egui::Color32 = egui::Color32::from_rgb(210, 62, 62);
const COLOR_INFO: egui::Color32 = egui::Color32::from_rgb(176, 111, 0);
const COLOR_STATUS: egui::Color32 = egui::Color32::from_rgb(249, 86, 79);
const TOOLBAR_BUTTON_HEIGHT: f32 = 46.0;
const START_BUTTON_SIZE: egui::Vec2 = egui::vec2(140.0, 48.0);

#[derive(PartialEq)]
enum AddingState {
    None,
    MouseClick,
    KeyPress,
    Delay,
}

pub struct App {
    actions: Vec<Action>,
    stop_condition: StopCondition,
    running: Arc<AtomicBool>,
    worker_handle: Option<JoinHandle<()>>,

    // UI state for adding actions
    adding: AddingState,

    // Mouse click form
    mouse_button: MouseButton,
    mouse_x: String,
    mouse_y: String,

    // Keyboard press form
    key_input: String,

    // Delay form
    delay_ms: String,

    // Stop timer input
    stop_seconds: String,

    // Hotkey manager
    hotkey_manager: Option<HotkeyManager>,
    hotkey_support: HotkeySupport,
    mouse_capture_support: MouseCaptureSupport,

    // Runtime status surfaced from background work
    status_message: Option<String>,
    status_tx: Sender<String>,
    status_rx: Receiver<String>,

    // F1 captured mouse position
    captured_position: Arc<(AtomicI32, AtomicI32)>,
    position_captured: Arc<AtomicBool>,
}

impl App {
    pub fn new(_cc: &eframe::CreationContext<'_>) -> Self {
        let (status_tx, status_rx) = mpsc::channel();
        let mut status_message = None;
        let hotkey_manager = match HotkeyManager::new() {
            Ok(manager) => Some(manager),
            Err(err) => {
                status_message = Some(err);
                None
            }
        };
        let hotkey_support = hotkey_manager
            .as_ref()
            .map(HotkeyManager::support)
            .unwrap_or_else(HotkeySupport::detect);
        let mouse_capture_support = MouseCaptureSupport::detect();
        Self {
            actions: Vec::new(),
            stop_condition: StopCondition::HotkeyOnly,
            running: Arc::new(AtomicBool::new(false)),
            worker_handle: None,
            adding: AddingState::None,
            mouse_button: MouseButton::Left,
            mouse_x: String::new(),
            mouse_y: String::new(),
            key_input: String::new(),
            delay_ms: String::new(),
            stop_seconds: String::from("120"),
            hotkey_manager,
            hotkey_support,
            mouse_capture_support,
            status_message,
            status_tx,
            status_rx,
            captured_position: Arc::new((AtomicI32::new(0), AtomicI32::new(0))),
            position_captured: Arc::new(AtomicBool::new(false)),
        }
    }

    fn toggle_adding(&mut self, state: AddingState) {
        if self.adding == state {
            self.adding = AddingState::None;
        } else {
            self.adding = state;
        }
    }

    fn apply_timer_stop(&mut self) {
        if let Ok(s) = self.stop_seconds.parse::<u64>() {
            self.stop_condition = StopCondition::Timer { seconds: s };
        }
    }

    fn action_color(action: &Action) -> egui::Color32 {
        match action {
            Action::MouseClick { .. } => COLOR_MOUSE,
            Action::KeyPress { .. } => COLOR_KEYBOARD,
            Action::Delay { .. } => COLOR_DELAY,
        }
    }

    fn toolbar_button(label: &str, accent: egui::Color32) -> egui::Button<'static> {
        egui::Button::new(
            egui::RichText::new(label)
                .size(16.0)
                .strong()
                .color(egui::Color32::WHITE),
        )
        .wrap()
        .fill(COLOR_TOOLBAR_FILL)
        .stroke(egui::Stroke::new(2.0, accent))
        .corner_radius(8)
        .min_size(egui::vec2(0.0, TOOLBAR_BUTTON_HEIGHT))
    }

    fn run_button(label: &str, fill: egui::Color32) -> egui::Button<'static> {
        egui::Button::new(
            egui::RichText::new(label)
                .size(18.0)
                .strong()
                .color(egui::Color32::WHITE),
        )
        .fill(fill)
        .stroke(egui::Stroke::new(1.0, fill.gamma_multiply(0.7)))
        .corner_radius(8)
        .min_size(START_BUTTON_SIZE)
    }

    fn worker_active(&self) -> bool {
        self.worker_handle.is_some()
    }

    fn poll_status_messages(&mut self) {
        while let Ok(message) = self.status_rx.try_recv() {
            self.status_message = Some(message);
        }
    }

    fn reap_worker(&mut self) {
        if self
            .worker_handle
            .as_ref()
            .is_some_and(|handle| handle.is_finished())
        {
            if let Some(handle) = self.worker_handle.take() {
                if handle.join().is_err() {
                    self.status_message = Some(String::from("Automation thread crashed."));
                }
            }

            self.running.store(false, Ordering::Release);
        }
    }

    fn start_worker(&mut self) {
        if self.worker_active() {
            return;
        }

        self.status_message = None;
        self.running.store(true, Ordering::Release);
        self.worker_handle = Some(executor::run_sequence(
            self.actions.clone(),
            self.stop_condition.clone(),
            Arc::clone(&self.running),
            self.status_tx.clone(),
        ));
    }

    fn stop_worker(&self) {
        self.running.store(false, Ordering::Release);
    }

    fn join_worker(&mut self) {
        if let Some(handle) = self.worker_handle.take() {
            if handle.join().is_err() {
                self.status_message = Some(String::from("Automation thread crashed."));
            }
        }

        self.running.store(false, Ordering::Release);
    }

    fn hotkey_support(&self) -> HotkeySupport {
        self.hotkey_support
    }

    fn mouse_capture_support(&self) -> MouseCaptureSupport {
        self.mouse_capture_support
    }

    fn mouse_capture_hint(&self) -> &'static str {
        if let Some(message) = self.mouse_capture_support().unsupported_message() {
            return message;
        }

        match self.hotkey_support() {
            HotkeySupport::Global => "Press F1 to capture",
            HotkeySupport::FocusedOnly => "Focus this window and press F1 to capture",
        }
    }

    fn platform_notice(&self) -> Option<&'static str> {
        match (self.hotkey_support(), self.mouse_capture_support()) {
            (HotkeySupport::FocusedOnly, MouseCaptureSupport::UnsupportedOnWayland) => Some(
                "Wayland detected: F2 stop works only while this window is focused, and mouse-position capture is unavailable in this build.",
            ),
            (HotkeySupport::FocusedOnly, MouseCaptureSupport::Available) => {
                self.hotkey_support().notice()
            }
            (HotkeySupport::Global, MouseCaptureSupport::UnsupportedOnWayland) => {
                self.mouse_capture_support().unsupported_message()
            }
            (HotkeySupport::Global, MouseCaptureSupport::Available) => None,
        }
    }

    fn capture_mouse_position(&mut self) {
        if let Some(message) = self.mouse_capture_support().unsupported_message() {
            self.status_message = Some(message.to_string());
            return;
        }

        match capture_mouse_position(&self.captured_position, &self.position_captured) {
            Ok(()) => {
                self.status_message = Some(String::from("Mouse position captured."));
            }
            Err(err) => {
                self.status_message = Some(err);
            }
        }
    }

    fn handle_focused_hotkeys(&mut self, ctx: &egui::Context, worker_active: bool) {
        if self.hotkey_support() != HotkeySupport::FocusedOnly {
            return;
        }

        let app_is_focused = ctx.input(|input| input.focused);
        if !app_is_focused {
            return;
        }

        let capture_requested = self.adding == AddingState::MouseClick
            && ctx.input_mut(|input| input.consume_key(egui::Modifiers::NONE, egui::Key::F1));
        if capture_requested {
            self.capture_mouse_position();
        }

        let stop_requested = worker_active
            && ctx.input_mut(|input| input.consume_key(egui::Modifiers::NONE, egui::Key::F2));
        if stop_requested {
            self.stop_worker();
            self.status_message =
                Some(String::from("Stop requested from the focused window (F2)."));
        }
    }
}

impl Drop for App {
    fn drop(&mut self) {
        self.running.store(false, Ordering::Release);
        self.join_worker();
    }
}

impl eframe::App for App {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        // Poll hotkeys
        if let Some(hotkey_manager) = &self.hotkey_manager {
            if let Some(message) = hotkey_manager.poll(
                &self.running,
                &self.captured_position,
                &self.position_captured,
            ) {
                self.status_message = Some(message);
            }
        }

        self.poll_status_messages();
        self.reap_worker();
        self.handle_focused_hotkeys(ctx, self.worker_active());

        // Check if F1 captured a position
        if self.position_captured.load(Ordering::Acquire) {
            self.mouse_x = self.captured_position.0.load(Ordering::Relaxed).to_string();
            self.mouse_y = self.captured_position.1.load(Ordering::Relaxed).to_string();
            self.position_captured.store(false, Ordering::Release);
        }

        let is_running = self.running.load(Ordering::Acquire);
        let worker_active = self.worker_active();
        let is_stopping = worker_active && !is_running;

        // Request repaint to keep polling hotkeys
        ctx.request_repaint_after(Duration::from_millis(100));

        egui::CentralPanel::default().show(ctx, |ui| {
            egui::ScrollArea::vertical()
                .auto_shrink([false, false])
                .show(ui, |ui| {
                    ui.spacing_mut().item_spacing.y = 8.0;

                    // === Top Toolbar ===
                    let button_spacing = ui.spacing().item_spacing.x;
                    let available_width = ui.available_width().max(0.0);
                    let button_width = ((available_width - (button_spacing * 2.0)) / 3.0).max(0.0);

                    ui.horizontal(|ui| {
                        let enabled = !worker_active;
                        ui.add_enabled_ui(enabled, |ui| {
                            if ui
                                .add_sized(
                                    [button_width, TOOLBAR_BUTTON_HEIGHT],
                                    Self::toolbar_button("Add Mouse Click", COLOR_MOUSE),
                                )
                                .clicked()
                            {
                                self.toggle_adding(AddingState::MouseClick);
                            }
                            if ui
                                .add_sized(
                                    [button_width, TOOLBAR_BUTTON_HEIGHT],
                                    Self::toolbar_button("Add Keyboard Press", COLOR_KEYBOARD),
                                )
                                .clicked()
                            {
                                self.toggle_adding(AddingState::KeyPress);
                            }
                            if ui
                                .add_sized(
                                    [button_width, TOOLBAR_BUTTON_HEIGHT],
                                    Self::toolbar_button("Add Delay", COLOR_DELAY),
                                )
                                .clicked()
                            {
                                self.toggle_adding(AddingState::Delay);
                            }
                        });
                    });

                    // === Inline Forms ===
                    match self.adding {
                        AddingState::MouseClick => {
                            ui.group(|ui| {
                                ui.label("Mouse Click:");
                                ui.horizontal(|ui| {
                                    ui.radio_value(
                                        &mut self.mouse_button,
                                        MouseButton::Left,
                                        "Left",
                                    );
                                    ui.radio_value(
                                        &mut self.mouse_button,
                                        MouseButton::Right,
                                        "Right",
                                    );
                                });
                                ui.horizontal(|ui| {
                                    ui.label("X:");
                                    ui.add(
                                        egui::TextEdit::singleline(&mut self.mouse_x)
                                            .desired_width(60.0),
                                    );
                                    ui.label("Y:");
                                    ui.add(
                                        egui::TextEdit::singleline(&mut self.mouse_y)
                                            .desired_width(60.0),
                                    );
                                });
                                ui.label(self.mouse_capture_hint());
                                if ui.button("Add").clicked() {
                                    if let (Ok(x), Ok(y)) =
                                        (self.mouse_x.parse::<i32>(), self.mouse_y.parse::<i32>())
                                    {
                                        self.actions.push(Action::MouseClick {
                                            button: self.mouse_button.clone(),
                                            x,
                                            y,
                                        });
                                        self.adding = AddingState::None;
                                    }
                                }
                            });
                        }
                        AddingState::KeyPress => {
                            ui.group(|ui| {
                                ui.label("Key Press:");
                                ui.horizontal(|ui| {
                                    let keys =
                                        ["1", "2", "3", "4", "5", "Space", "Enter", "Tab", "Esc"];
                                    for key in &keys {
                                        if ui.button(*key).clicked() {
                                            self.key_input = key.to_lowercase();
                                        }
                                    }
                                });
                                ui.horizontal(|ui| {
                                    ui.label("Or type key:");
                                    ui.add(
                                        egui::TextEdit::singleline(&mut self.key_input)
                                            .desired_width(80.0),
                                    );
                                });
                                if ui.button("Add").clicked() && !self.key_input.is_empty() {
                                    self.actions.push(Action::KeyPress {
                                        key: self.key_input.to_lowercase(),
                                    });
                                    self.key_input.clear();
                                    self.adding = AddingState::None;
                                }
                            });
                        }
                        AddingState::Delay => {
                            ui.group(|ui| {
                                ui.label("Delay:");
                                ui.horizontal(|ui| {
                                    ui.add(
                                        egui::TextEdit::singleline(&mut self.delay_ms)
                                            .desired_width(80.0),
                                    );
                                    ui.label("ms");
                                });
                                ui.horizontal(|ui| {
                                    for (label, ms) in [
                                        ("500ms", "500"),
                                        ("1s", "1000"),
                                        ("2s", "2000"),
                                        ("5s", "5000"),
                                        ("10s", "10000"),
                                    ] {
                                        if ui.button(label).clicked() {
                                            self.delay_ms = ms.to_string();
                                        }
                                    }
                                });
                                if ui.button("Add").clicked() {
                                    if let Ok(ms) = self.delay_ms.parse::<u64>() {
                                        self.actions.push(Action::Delay { ms });
                                        self.delay_ms.clear();
                                        self.adding = AddingState::None;
                                    }
                                }
                            });
                        }
                        AddingState::None => {}
                    }

                    ui.separator();

                    // === Action List ===
                    ui.label(egui::RichText::new("Loop Order:").strong().size(16.0));

                    if self.actions.is_empty() {
                        ui.label("No actions added yet.");
                    } else {
                        let mut to_remove: Option<usize> = None;
                        let mut to_move: Option<(usize, isize)> = None;

                        egui::ScrollArea::vertical()
                            .max_height(250.0)
                            .show(ui, |ui| {
                                for (i, action) in self.actions.iter().enumerate() {
                                    ui.horizontal(|ui| {
                                        let color = Self::action_color(action);
                                        ui.label(
                                            egui::RichText::new(format!("{}. {}", i + 1, action))
                                                .color(color)
                                                .strong(),
                                        );

                                        if !worker_active {
                                            ui.with_layout(
                                                egui::Layout::right_to_left(egui::Align::Center),
                                                |ui| {
                                                    if ui.small_button("X").clicked() {
                                                        to_remove = Some(i);
                                                    }
                                                    if i < self.actions.len() - 1
                                                        && ui.small_button("v").clicked()
                                                    {
                                                        to_move = Some((i, 1));
                                                    }
                                                    if i > 0 && ui.small_button("^").clicked() {
                                                        to_move = Some((i, -1));
                                                    }
                                                },
                                            );
                                        }
                                    });
                                }
                            });

                        if let Some(idx) = to_remove {
                            self.actions.remove(idx);
                        }
                        if let Some((idx, dir)) = to_move {
                            let new_idx = (idx as isize + dir) as usize;
                            self.actions.swap(idx, new_idx);
                        }
                    }

                    ui.separator();

                    // === Stop Configuration ===
                    ui.add_enabled_ui(!worker_active, |ui| {
                        ui.label(egui::RichText::new("Stop Condition:").strong());
                        let mut is_hotkey_only = self.stop_condition == StopCondition::HotkeyOnly;
                        if ui
                            .radio_value(
                                &mut is_hotkey_only,
                                true,
                                match self.hotkey_support() {
                                    HotkeySupport::Global => "Stop on F2 press only",
                                    HotkeySupport::FocusedOnly => "Stop on focused F2 press only",
                                },
                            )
                            .clicked()
                        {
                            self.stop_condition = StopCondition::HotkeyOnly;
                        }
                        ui.horizontal(|ui| {
                            if ui
                                .radio_value(&mut is_hotkey_only, false, "Stop after")
                                .clicked()
                            {
                                self.apply_timer_stop();
                            }
                            let response = ui.add_enabled(
                                !is_hotkey_only,
                                egui::TextEdit::singleline(&mut self.stop_seconds)
                                    .desired_width(50.0),
                            );
                            ui.label(self.hotkey_support().stop_hint());
                            if response.changed() {
                                self.apply_timer_stop();
                            }
                        });
                    });

                    if let Some(notice) = self.platform_notice() {
                        ui.separator();
                        ui.colored_label(COLOR_INFO, notice);
                    }

                    if let Some(message) = &self.status_message {
                        ui.separator();
                        ui.colored_label(COLOR_STATUS, message);
                    }

                    ui.separator();

                    // === Start/Stop Button ===
                    ui.vertical_centered(|ui| {
                        if is_running {
                            if ui.add(Self::run_button("STOP", COLOR_STOP)).clicked() {
                                self.stop_worker();
                            }
                        } else if is_stopping {
                            ui.add_enabled(false, Self::run_button("Stopping...", COLOR_STOP));
                        } else if !self.actions.is_empty()
                            && ui.add(Self::run_button("START", COLOR_START)).clicked()
                        {
                            self.start_worker();
                        }
                    });
                });
        });
    }
}
