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
const CRASH_MESSAGE: &str = "Automation thread crashed.";
const TOOLBAR_BUTTON_HEIGHT: f32 = 46.0;
const START_BUTTON_SIZE: egui::Vec2 = egui::vec2(140.0, 48.0);
const TOOLBAR_STACK_BREAKPOINT: f32 = 440.0;
const FORM_STACK_BREAKPOINT: f32 = 360.0;
const ACTION_ROW_STACK_BREAKPOINT: f32 = 340.0;
const ACTION_CONTROL_WIDTH: f32 = 72.0;

#[derive(Debug, PartialEq, Eq)]
enum AddingState {
    None,
    MouseClick,
    KeyPress,
    Delay,
}

#[derive(Clone, Copy, Default)]
struct ViewportRestoreState {
    outer_position: Option<egui::Pos2>,
    inner_size: Option<egui::Vec2>,
    maximized: bool,
    fullscreen: bool,
}

#[derive(Clone, Copy, Default)]
struct MouseCapturePickerSession {
    restore_state: ViewportRestoreState,
    waiting_for_release: bool,
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
    mouse_capture_picker: Option<MouseCapturePickerSession>,

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
        Self::build(
            hotkey_manager,
            hotkey_support,
            MouseCaptureSupport::detect(),
            status_message,
        )
    }

    fn build(
        hotkey_manager: Option<HotkeyManager>,
        hotkey_support: HotkeySupport,
        mouse_capture_support: MouseCaptureSupport,
        status_message: Option<String>,
    ) -> Self {
        let (status_tx, status_rx) = mpsc::channel();
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
            mouse_capture_picker: None,
            status_message,
            status_tx,
            status_rx,
            captured_position: Arc::new((AtomicI32::new(0), AtomicI32::new(0))),
            position_captured: Arc::new(AtomicBool::new(false)),
        }
    }

    #[cfg(test)]
    fn new_for_tests() -> Self {
        Self::build(
            None,
            HotkeySupport::FocusedOnly,
            MouseCaptureSupport::Direct,
            None,
        )
    }

    fn toggle_adding(&mut self, state: AddingState) {
        if self.adding == state {
            self.adding = AddingState::None;
        } else {
            match state {
                AddingState::MouseClick => self.open_mouse_click_form(),
                _ => self.adding = state,
            }
        }
    }

    fn open_mouse_click_form(&mut self) {
        self.mouse_x.clear();
        self.mouse_y.clear();
        self.adding = AddingState::MouseClick;
    }

    fn finish_mouse_click_add(&mut self, x: i32, y: i32) {
        self.actions.push(Action::MouseClick {
            button: self.mouse_button,
            x,
            y,
        });
        self.mouse_x.clear();
        self.mouse_y.clear();
        self.adding = AddingState::None;
    }

    fn try_add_mouse_click(&mut self) -> bool {
        if let (Ok(x), Ok(y)) = (self.mouse_x.parse::<i32>(), self.mouse_y.parse::<i32>()) {
            self.finish_mouse_click_add(x, y);
            true
        } else {
            false
        }
    }

    fn try_add_key_press(&mut self) -> bool {
        if self.key_input.is_empty() {
            return false;
        }

        self.actions.push(Action::KeyPress {
            key: self.key_input.to_lowercase(),
        });
        self.key_input.clear();
        self.adding = AddingState::None;
        true
    }

    fn try_add_delay(&mut self) -> bool {
        if let Ok(ms) = self.delay_ms.parse::<u64>() {
            self.actions.push(Action::Delay { ms });
            self.delay_ms.clear();
            self.adding = AddingState::None;
            true
        } else {
            false
        }
    }

    fn apply_timer_stop(&mut self) {
        if let Ok(s) = self.stop_seconds.parse::<u64>() {
            self.stop_condition = StopCondition::Timer { seconds: s };
        }
    }

    fn remove_action(&mut self, idx: usize) -> bool {
        if idx < self.actions.len() {
            self.actions.remove(idx);
            true
        } else {
            false
        }
    }

    fn move_action(&mut self, idx: usize, dir: isize) -> bool {
        let new_idx = idx as isize + dir;
        if idx < self.actions.len() && (0..self.actions.len() as isize).contains(&new_idx) {
            self.actions.swap(idx, new_idx as usize);
            true
        } else {
            false
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

    fn toolbar_stacks(available_width: f32) -> bool {
        available_width < TOOLBAR_STACK_BREAKPOINT
    }

    fn form_stacks(available_width: f32) -> bool {
        available_width < FORM_STACK_BREAKPOINT
    }

    fn action_row_stacks(available_width: f32) -> bool {
        available_width < ACTION_ROW_STACK_BREAKPOINT
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
                    self.status_message = Some(String::from(CRASH_MESSAGE));
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
                self.status_message = Some(String::from(CRASH_MESSAGE));
            }
        }

        self.running.store(false, Ordering::Release);
    }

    fn apply_captured_position(&mut self) {
        self.mouse_x = self.captured_position.0.load(Ordering::Relaxed).to_string();
        self.mouse_y = self.captured_position.1.load(Ordering::Relaxed).to_string();
    }

    fn mouse_capture_hint(&self) -> &'static str {
        match (self.mouse_capture_support, self.hotkey_support) {
            (MouseCaptureSupport::Direct, HotkeySupport::Global) => {
                "Press F1 to capture the current mouse position, or click Pick On Screen to choose a point visually"
            }
            (MouseCaptureSupport::Direct, HotkeySupport::FocusedOnly) => {
                "Focus this window and press F1 to capture the current mouse position, or click Pick On Screen to choose a point visually"
            }
            (MouseCaptureSupport::Picker, HotkeySupport::Global) => {
                "Press F1 or click Pick On Screen, then click the target point on this monitor"
            }
            (MouseCaptureSupport::Picker, HotkeySupport::FocusedOnly) => {
                "Focus this window and press F1, or click Pick On Screen, then click the target point on this monitor"
            }
        }
    }

    fn mouse_capture_button_label(&self) -> &'static str {
        "Pick On Screen"
    }

    fn platform_notice(&self) -> Option<&'static str> {
        match (self.hotkey_support, self.mouse_capture_support) {
            (HotkeySupport::FocusedOnly, MouseCaptureSupport::Picker) => Some(
                "Wayland detected: F2 stop works only while this window is focused, and mouse capture uses a transparent overlay on this window's monitor for windowed or borderless apps.",
            ),
            (HotkeySupport::FocusedOnly, MouseCaptureSupport::Direct) => {
                self.hotkey_support.notice()
            }
            (HotkeySupport::Global, MouseCaptureSupport::Picker) => {
                self.mouse_capture_support.notice()
            }
            (HotkeySupport::Global, MouseCaptureSupport::Direct) => None,
        }
    }

    fn current_viewport_restore_state(ctx: &egui::Context) -> ViewportRestoreState {
        ctx.input(|input| {
            let viewport = input.viewport();
            ViewportRestoreState {
                outer_position: viewport.outer_rect.map(|rect| rect.min),
                inner_size: viewport.inner_rect.map(|rect| rect.size()),
                maximized: viewport.maximized.unwrap_or(false),
                fullscreen: viewport.fullscreen.unwrap_or(false),
            }
        })
    }

    fn restore_viewport_state(ctx: &egui::Context, previous: ViewportRestoreState) {
        ctx.send_viewport_cmd(egui::ViewportCommand::Fullscreen(previous.fullscreen));
        ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(previous.maximized));
        ctx.send_viewport_cmd(egui::ViewportCommand::Decorations(true));
        ctx.send_viewport_cmd(egui::ViewportCommand::Resizable(true));

        if !previous.fullscreen && !previous.maximized {
            if let Some(inner_size) = previous.inner_size {
                ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(inner_size));
            }
            if let Some(outer_position) = previous.outer_position {
                ctx.send_viewport_cmd(egui::ViewportCommand::OuterPosition(outer_position));
            }
        }
    }

    fn open_mouse_capture_picker(&mut self, restore_state: ViewportRestoreState) {
        self.mouse_capture_picker = Some(MouseCapturePickerSession {
            restore_state,
            waiting_for_release: true,
        });
    }

    fn start_mouse_capture_picker(&mut self, ctx: &egui::Context) {
        if self.mouse_capture_picker.is_some() {
            return;
        }

        self.open_mouse_capture_picker(Self::current_viewport_restore_state(ctx));
        self.status_message = Some(String::from(
            "Click the target point on this monitor to capture the mouse position. This overlay is intended for windowed or borderless apps. Press Esc to cancel.",
        ));
        ctx.send_viewport_cmd(egui::ViewportCommand::Fullscreen(false));
        ctx.send_viewport_cmd(egui::ViewportCommand::Decorations(false));
        ctx.send_viewport_cmd(egui::ViewportCommand::Resizable(false));
        ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(true));
        ctx.send_viewport_cmd(egui::ViewportCommand::Transparent(true));
        ctx.request_repaint();
    }

    fn picker_pos_to_pixels(ctx: &egui::Context, pos: egui::Pos2) -> (i32, i32) {
        ctx.input(|input| {
            let viewport = input.viewport();
            let scale = viewport
                .native_pixels_per_point
                .unwrap_or_else(|| ctx.pixels_per_point())
                .max(1.0);
            let absolute_pos = viewport
                .inner_rect
                .map(|rect| rect.min + pos.to_vec2())
                .unwrap_or(pos);
            let x = (absolute_pos.x * scale).round();
            let y = (absolute_pos.y * scale).round();
            (x as i32, y as i32)
        })
    }

    fn capture_mouse_position(&mut self) {
        match capture_mouse_position(&self.captured_position, &self.position_captured) {
            Ok(()) => {
                self.apply_captured_position();
                self.position_captured.store(false, Ordering::Release);
                self.status_message = Some(String::from("Mouse position captured."));
            }
            Err(err) => {
                self.status_message = Some(err);
            }
        }
    }

    fn take_mouse_capture_picker_restore_state(&mut self) -> Option<ViewportRestoreState> {
        self.mouse_capture_picker
            .take()
            .map(|session| session.restore_state)
    }

    fn restore_viewport(&mut self, ctx: &egui::Context) {
        let Some(previous) = self.take_mouse_capture_picker_restore_state() else {
            return;
        };

        Self::restore_viewport_state(ctx, previous);
    }

    fn begin_mouse_capture_from_button(&mut self, ctx: &egui::Context) {
        self.start_mouse_capture_picker(ctx);
    }

    fn begin_mouse_capture_from_hotkey(&mut self, ctx: &egui::Context) {
        match self.mouse_capture_support {
            MouseCaptureSupport::Direct => self.capture_mouse_position(),
            MouseCaptureSupport::Picker => self.start_mouse_capture_picker(ctx),
        }
    }

    fn apply_mouse_capture_picker_selection(&mut self, x: i32, y: i32) {
        self.mouse_x = x.to_string();
        self.mouse_y = y.to_string();
        self.status_message = Some(format!("Mouse position captured at ({x}, {y})."));
    }

    fn complete_mouse_capture_picker(&mut self, ctx: &egui::Context, pos: egui::Pos2) {
        let (x, y) = Self::picker_pos_to_pixels(ctx, pos);
        let previous = self.take_mouse_capture_picker_restore_state();
        self.apply_mouse_capture_picker_selection(x, y);
        if let Some(previous) = previous {
            Self::restore_viewport_state(ctx, previous);
        }
    }

    fn cancel_mouse_capture_picker(&mut self, ctx: &egui::Context) {
        self.restore_viewport(ctx);
        self.status_message = Some(String::from("Mouse position capture cancelled."));
    }

    fn picker_waiting_for_release(&self) -> bool {
        self.mouse_capture_picker
            .as_ref()
            .map(|session| session.waiting_for_release)
            .unwrap_or(false)
    }

    fn arm_mouse_capture_picker_after_release(&mut self, primary_down: bool) {
        if primary_down {
            return;
        }

        if let Some(session) = self.mouse_capture_picker.as_mut() {
            session.waiting_for_release = false;
        }
    }

    fn render_mouse_capture_picker(&mut self, ctx: &egui::Context) {
        let cancel_requested = ctx.input(|input| {
            input.key_pressed(egui::Key::Escape) || input.viewport().close_requested()
        });
        if cancel_requested {
            self.cancel_mouse_capture_picker(ctx);
            return;
        }

        let captured_pos = if self.picker_waiting_for_release() {
            let primary_down = ctx.input(|input| input.pointer.primary_down());
            self.arm_mouse_capture_picker_after_release(primary_down);
            None
        } else {
            ctx.input(|input| {
                if input.pointer.primary_clicked() {
                    input.pointer.interact_pos()
                } else {
                    None
                }
            })
        };
        if let Some(pos) = captured_pos {
            self.complete_mouse_capture_picker(ctx, pos);
            return;
        }

        let pointer_pos = ctx.input(|input| input.pointer.latest_pos());
        let preview = pointer_pos.map(|pos| Self::picker_pos_to_pixels(ctx, pos));
        ctx.request_repaint_after(Duration::from_millis(16));

        egui::CentralPanel::default()
            .frame(egui::Frame::NONE)
            .show(ctx, |ui| {
                let rect = ui.max_rect();
                let painter = ui.painter();

                if let Some(pos) = pointer_pos {
                    let stroke = egui::Stroke::new(1.5, COLOR_MOUSE);
                    painter.line_segment(
                        [
                            egui::pos2(rect.left(), pos.y),
                            egui::pos2(rect.right(), pos.y),
                        ],
                        stroke,
                    );
                    painter.line_segment(
                        [
                            egui::pos2(pos.x, rect.top()),
                            egui::pos2(pos.x, rect.bottom()),
                        ],
                        stroke,
                    );
                    painter.circle_stroke(pos, 12.0, egui::Stroke::new(2.0, egui::Color32::WHITE));
                }
            });

        egui::Area::new(egui::Id::new("mouse_capture_picker_hud"))
            .anchor(egui::Align2::CENTER_TOP, egui::vec2(0.0, 24.0))
            .show(ctx, |ui| {
                egui::Frame::new()
                    .fill(egui::Color32::from_rgba_unmultiplied(8, 12, 18, 220))
                    .stroke(egui::Stroke::new(1.0, COLOR_MOUSE.gamma_multiply(0.8)))
                    .corner_radius(12)
                    .inner_margin(egui::Margin::symmetric(18, 14))
                    .show(ui, |ui| {
                        ui.vertical_centered(|ui| {
                            ui.label(
                                egui::RichText::new("Pick Mouse Position")
                                    .size(24.0)
                                    .strong()
                                    .color(egui::Color32::WHITE),
                            );
                            ui.label(
                                egui::RichText::new(
                                    "Click the target point on this monitor. Designed for windowed or borderless apps. Press Esc to cancel.",
                                )
                                .size(16.0)
                                .color(egui::Color32::from_gray(225)),
                            );
                            let preview_text = match preview {
                                Some((x, y)) => format!("Preview: ({x}, {y})"),
                                None => String::from("Move the cursor to preview coordinates"),
                            };
                            ui.add_space(4.0);
                            ui.label(
                                egui::RichText::new(preview_text)
                                    .size(20.0)
                                    .strong()
                                    .color(COLOR_MOUSE),
                            );
                        });
                    });
            });
    }

    fn handle_focused_hotkeys(&mut self, ctx: &egui::Context, worker_active: bool) {
        if self.mouse_capture_picker.is_some() {
            return;
        }

        if self.hotkey_support != HotkeySupport::FocusedOnly {
            return;
        }

        let app_is_focused = ctx.input(|input| input.focused);
        if !app_is_focused {
            return;
        }

        let capture_requested = self.adding == AddingState::MouseClick
            && ctx.input_mut(|input| input.consume_key(egui::Modifiers::NONE, egui::Key::F1));
        if capture_requested {
            self.begin_mouse_capture_from_hotkey(ctx);
        }

        let stop_requested = worker_active
            && ctx.input_mut(|input| input.consume_key(egui::Modifiers::NONE, egui::Key::F2));
        if stop_requested {
            self.stop_worker();
            self.status_message =
                Some(String::from("Stop requested from the focused window (F2)."));
        }
    }

    fn render_toolbar(&mut self, ui: &mut egui::Ui, worker_active: bool) {
        let available_width = ui.available_width().max(0.0);

        ui.add_enabled_ui(!worker_active, |ui| {
            if Self::toolbar_stacks(available_width) {
                for (label, accent, state) in [
                    ("Add Mouse Click", COLOR_MOUSE, AddingState::MouseClick),
                    ("Add Keyboard Press", COLOR_KEYBOARD, AddingState::KeyPress),
                    ("Add Delay", COLOR_DELAY, AddingState::Delay),
                ] {
                    if ui
                        .add_sized(
                            [ui.available_width(), TOOLBAR_BUTTON_HEIGHT],
                            Self::toolbar_button(label, accent),
                        )
                        .clicked()
                    {
                        self.toggle_adding(state);
                    }
                }
            } else {
                let button_spacing = ui.spacing().item_spacing.x;
                let button_width = ((available_width - (button_spacing * 2.0)) / 3.0).max(0.0);

                ui.horizontal(|ui| {
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
            }
        });
    }

    fn render_mouse_click_form(&mut self, ui: &mut egui::Ui) {
        ui.group(|ui| {
            ui.label("Mouse Click:");
            ui.horizontal_wrapped(|ui| {
                ui.radio_value(&mut self.mouse_button, MouseButton::Left, "Left");
                ui.radio_value(&mut self.mouse_button, MouseButton::Right, "Right");
            });

            if Self::form_stacks(ui.available_width()) {
                ui.label("X:");
                ui.add_sized(
                    [ui.available_width(), 0.0],
                    egui::TextEdit::singleline(&mut self.mouse_x),
                );
                ui.label("Y:");
                ui.add_sized(
                    [ui.available_width(), 0.0],
                    egui::TextEdit::singleline(&mut self.mouse_y),
                );

                if ui
                    .add_sized(
                        [ui.available_width(), 0.0],
                        egui::Button::new(self.mouse_capture_button_label()),
                    )
                    .clicked()
                {
                    self.begin_mouse_capture_from_button(ui.ctx());
                }
                ui.label(self.mouse_capture_hint());
            } else {
                let field_width =
                    ((ui.available_width() - ui.spacing().item_spacing.x) / 2.0).max(120.0);

                ui.horizontal(|ui| {
                    ui.vertical(|ui| {
                        ui.label("X:");
                        ui.add_sized(
                            [field_width, 0.0],
                            egui::TextEdit::singleline(&mut self.mouse_x),
                        );
                    });
                    ui.vertical(|ui| {
                        ui.label("Y:");
                        ui.add_sized(
                            [field_width, 0.0],
                            egui::TextEdit::singleline(&mut self.mouse_y),
                        );
                    });
                });

                ui.horizontal_wrapped(|ui| {
                    if ui.button(self.mouse_capture_button_label()).clicked() {
                        self.begin_mouse_capture_from_button(ui.ctx());
                    }
                    ui.label(self.mouse_capture_hint());
                });
            }

            ui.label(
                egui::RichText::new(
                    "Pick On Screen opens a transparent overlay on this window's monitor. Move this app onto the target monitor first.",
                )
                .color(COLOR_INFO),
            );

            if ui
                .add_sized([ui.available_width(), 0.0], egui::Button::new("Add"))
                .clicked()
            {
                self.try_add_mouse_click();
            }
        });
    }

    fn render_key_press_form(&mut self, ui: &mut egui::Ui) {
        ui.group(|ui| {
            ui.label("Key Press:");
            ui.horizontal_wrapped(|ui| {
                let keys = ["1", "2", "3", "4", "5", "Space", "Enter", "Tab", "Esc"];
                for key in &keys {
                    if ui.button(*key).clicked() {
                        self.key_input = key.to_lowercase();
                    }
                }
            });

            if Self::form_stacks(ui.available_width()) {
                ui.label("Or type key:");
                ui.add_sized(
                    [ui.available_width(), 0.0],
                    egui::TextEdit::singleline(&mut self.key_input),
                );
            } else {
                ui.horizontal(|ui| {
                    ui.label("Or type key:");
                    ui.add_sized(
                        [ui.available_width().min(160.0), 0.0],
                        egui::TextEdit::singleline(&mut self.key_input),
                    );
                });
            }

            if ui
                .add_sized([ui.available_width(), 0.0], egui::Button::new("Add"))
                .clicked()
            {
                self.try_add_key_press();
            }
        });
    }

    fn render_delay_form(&mut self, ui: &mut egui::Ui) {
        ui.group(|ui| {
            ui.label("Delay:");

            if Self::form_stacks(ui.available_width()) {
                ui.label("Milliseconds:");
                ui.add_sized(
                    [ui.available_width(), 0.0],
                    egui::TextEdit::singleline(&mut self.delay_ms),
                );
            } else {
                ui.horizontal(|ui| {
                    ui.add_sized(
                        [ui.available_width().min(160.0), 0.0],
                        egui::TextEdit::singleline(&mut self.delay_ms),
                    );
                    ui.label("ms");
                });
            }

            ui.horizontal_wrapped(|ui| {
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

            if ui
                .add_sized([ui.available_width(), 0.0], egui::Button::new("Add"))
                .clicked()
            {
                self.try_add_delay();
            }
        });
    }

    fn render_action_list(&mut self, ui: &mut egui::Ui, worker_active: bool) {
        ui.label(egui::RichText::new("Loop Order:").strong().size(16.0));

        if self.actions.is_empty() {
            ui.label("No actions added yet.");
            return;
        }

        let mut to_remove: Option<usize> = None;
        let mut to_move: Option<(usize, isize)> = None;

        egui::ScrollArea::vertical()
            .max_height(250.0)
            .show(ui, |ui| {
                for (i, action) in self.actions.iter().enumerate() {
                    let color = Self::action_color(action);
                    let label = egui::RichText::new(format!("{}. {}", i + 1, action))
                        .color(color)
                        .strong();

                    ui.group(|ui| {
                        if Self::action_row_stacks(ui.available_width()) {
                            ui.add(egui::Label::new(label.clone()).wrap());

                            if !worker_active {
                                ui.horizontal_wrapped(|ui| {
                                    if i > 0 && ui.small_button("^").clicked() {
                                        to_move = Some((i, -1));
                                    }
                                    if i < self.actions.len() - 1 && ui.small_button("v").clicked()
                                    {
                                        to_move = Some((i, 1));
                                    }
                                    if ui.small_button("X").clicked() {
                                        to_remove = Some(i);
                                    }
                                });
                            }
                        } else {
                            let label_width = if worker_active {
                                ui.available_width()
                            } else {
                                (ui.available_width() - ACTION_CONTROL_WIDTH).max(0.0)
                            };

                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    [label_width, 0.0],
                                    egui::Label::new(label.clone()).wrap(),
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
                }
            });

        if let Some(idx) = to_remove {
            self.remove_action(idx);
        }
        if let Some((idx, dir)) = to_move {
            self.move_action(idx, dir);
        }
    }

    fn render_stop_configuration(&mut self, ui: &mut egui::Ui, worker_active: bool) {
        ui.add_enabled_ui(!worker_active, |ui| {
            ui.label(egui::RichText::new("Stop Condition:").strong());
            let mut is_hotkey_only = self.stop_condition == StopCondition::HotkeyOnly;

            if ui
                .radio_value(
                    &mut is_hotkey_only,
                    true,
                    match self.hotkey_support {
                        HotkeySupport::Global => "Stop on F2 press only",
                        HotkeySupport::FocusedOnly => "Stop on focused F2 press only",
                    },
                )
                .clicked()
            {
                self.stop_condition = StopCondition::HotkeyOnly;
            }

            let stop_hint = self.hotkey_support.stop_hint();
            if Self::form_stacks(ui.available_width()) {
                if ui
                    .radio_value(&mut is_hotkey_only, false, "Stop after")
                    .clicked()
                {
                    self.apply_timer_stop();
                }
                let response = ui.add_enabled(
                    !is_hotkey_only,
                    egui::TextEdit::singleline(&mut self.stop_seconds),
                );
                ui.label(stop_hint);
                if response.changed() {
                    self.apply_timer_stop();
                }
            } else {
                ui.horizontal_wrapped(|ui| {
                    if ui
                        .radio_value(&mut is_hotkey_only, false, "Stop after")
                        .clicked()
                    {
                        self.apply_timer_stop();
                    }
                    let response = ui.add_enabled(
                        !is_hotkey_only,
                        egui::TextEdit::singleline(&mut self.stop_seconds).desired_width(50.0),
                    );
                    ui.label(stop_hint);
                    if response.changed() {
                        self.apply_timer_stop();
                    }
                });
            }
        });
    }
}

impl Drop for App {
    fn drop(&mut self) {
        self.running.store(false, Ordering::Release);
        self.join_worker();
    }
}

impl eframe::App for App {
    fn clear_color(&self, _visuals: &egui::Visuals) -> [f32; 4] {
        egui::Color32::TRANSPARENT.to_normalized_gamma_f32()
    }

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
            self.apply_captured_position();
            self.position_captured.store(false, Ordering::Release);
        }

        let is_running = self.running.load(Ordering::Acquire);
        let worker_active = self.worker_active();
        let is_stopping = worker_active && !is_running;

        // Request repaint to keep polling hotkeys
        ctx.request_repaint_after(Duration::from_millis(100));

        if self.mouse_capture_picker.is_some() {
            self.render_mouse_capture_picker(ctx);
            return;
        }

        egui::CentralPanel::default().show(ctx, |ui| {
            egui::ScrollArea::vertical()
                .auto_shrink([false, false])
                .show(ui, |ui| {
                    ui.spacing_mut().item_spacing.y = 8.0;

                    self.render_toolbar(ui, worker_active);

                    // === Inline Forms ===
                    match self.adding {
                        AddingState::MouseClick => self.render_mouse_click_form(ui),
                        AddingState::KeyPress => self.render_key_press_form(ui),
                        AddingState::Delay => self.render_delay_form(ui),
                        AddingState::None => {}
                    }

                    ui.separator();

                    self.render_action_list(ui, worker_active);

                    ui.separator();

                    self.render_stop_configuration(ui, worker_active);

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

#[cfg(test)]
mod tests {
    use super::*;
    use std::thread;

    #[test]
    fn opening_mouse_click_form_clears_saved_coordinates() {
        let mut app = App::new_for_tests();
        app.mouse_x = String::from("120");
        app.mouse_y = String::from("340");

        app.toggle_adding(AddingState::MouseClick);

        assert_eq!(app.adding, AddingState::MouseClick);
        assert!(app.mouse_x.is_empty());
        assert!(app.mouse_y.is_empty());
    }

    #[test]
    fn adding_mouse_click_clears_coordinates_and_closes_form() {
        let mut app = App::new_for_tests();
        app.mouse_button = MouseButton::Right;
        app.open_mouse_click_form();
        app.mouse_x = String::from("45");
        app.mouse_y = String::from("90");

        assert!(app.try_add_mouse_click());
        assert_eq!(app.adding, AddingState::None);
        assert!(app.mouse_x.is_empty());
        assert!(app.mouse_y.is_empty());
        assert_eq!(app.actions.len(), 1);

        match &app.actions[0] {
            Action::MouseClick { button, x, y } => {
                assert_eq!(*button, MouseButton::Right);
                assert_eq!(*x, 45);
                assert_eq!(*y, 90);
            }
            action => panic!("expected mouse click action, got {action:?}"),
        }
    }

    #[test]
    fn non_mouse_forms_do_not_reset_coordinates() {
        let mut app = App::new_for_tests();
        app.mouse_x = String::from("12");
        app.mouse_y = String::from("34");

        app.toggle_adding(AddingState::KeyPress);
        assert_eq!(app.adding, AddingState::KeyPress);
        assert_eq!(app.mouse_x, "12");
        assert_eq!(app.mouse_y, "34");

        app.toggle_adding(AddingState::Delay);
        assert_eq!(app.adding, AddingState::Delay);
        assert_eq!(app.mouse_x, "12");
        assert_eq!(app.mouse_y, "34");
    }

    #[test]
    fn invalid_mouse_click_coordinates_leave_form_open() {
        let mut app = App::new_for_tests();
        app.open_mouse_click_form();
        app.mouse_x = String::from("12");
        app.mouse_y = String::from("abc");

        assert!(!app.try_add_mouse_click());
        assert_eq!(app.adding, AddingState::MouseClick);
        assert!(app.actions.is_empty());
    }

    #[test]
    fn adding_key_press_normalizes_input_and_closes_form() {
        let mut app = App::new_for_tests();
        app.adding = AddingState::KeyPress;
        app.key_input = String::from("EsC");

        assert!(app.try_add_key_press());
        assert_eq!(app.adding, AddingState::None);
        assert!(app.key_input.is_empty());
        assert_eq!(app.actions.len(), 1);
        assert_eq!(
            app.actions[0],
            Action::KeyPress {
                key: String::from("esc"),
            }
        );
    }

    #[test]
    fn empty_key_press_is_rejected() {
        let mut app = App::new_for_tests();
        app.adding = AddingState::KeyPress;

        assert!(!app.try_add_key_press());
        assert_eq!(app.adding, AddingState::KeyPress);
        assert!(app.actions.is_empty());
    }

    #[test]
    fn adding_delay_clears_input_and_closes_form() {
        let mut app = App::new_for_tests();
        app.adding = AddingState::Delay;
        app.delay_ms = String::from("1500");

        assert!(app.try_add_delay());
        assert_eq!(app.adding, AddingState::None);
        assert!(app.delay_ms.is_empty());
        assert_eq!(app.actions.len(), 1);
        assert_eq!(app.actions[0], Action::Delay { ms: 1500 });
    }

    #[test]
    fn invalid_delay_is_rejected() {
        let mut app = App::new_for_tests();
        app.adding = AddingState::Delay;
        app.delay_ms = String::from("1.5");

        assert!(!app.try_add_delay());
        assert_eq!(app.adding, AddingState::Delay);
        assert!(app.actions.is_empty());
    }

    #[test]
    fn invalid_timer_input_leaves_stop_condition_unchanged() {
        let mut app = App::new_for_tests();
        app.stop_condition = StopCondition::Timer { seconds: 25 };
        app.stop_seconds = String::from("two minutes");

        app.apply_timer_stop();

        assert_eq!(app.stop_condition, StopCondition::Timer { seconds: 25 });
    }

    #[test]
    fn remove_action_ignores_invalid_index() {
        let mut app = App::new_for_tests();
        app.actions.push(Action::Delay { ms: 100 });

        assert!(!app.remove_action(3));
        assert_eq!(app.actions.len(), 1);
    }

    #[test]
    fn move_action_swaps_items_and_rejects_out_of_bounds_moves() {
        let mut app = App::new_for_tests();
        app.actions = vec![
            Action::Delay { ms: 100 },
            Action::KeyPress {
                key: String::from("space"),
            },
            Action::Delay { ms: 200 },
        ];

        assert!(app.move_action(1, -1));
        assert_eq!(
            app.actions[0],
            Action::KeyPress {
                key: String::from("space"),
            }
        );
        assert!(!app.move_action(0, -1));
        assert!(!app.move_action(2, 1));
    }

    #[test]
    fn poll_status_messages_keeps_latest_message() {
        let mut app = App::new_for_tests();
        app.status_tx.send(String::from("first")).unwrap();
        app.status_tx.send(String::from("second")).unwrap();

        app.poll_status_messages();

        assert_eq!(app.status_message.as_deref(), Some("second"));
    }

    #[test]
    fn reap_worker_reports_crash_and_clears_running_flag() {
        let handle = thread::spawn(|| panic!("boom"));
        while !handle.is_finished() {
            thread::yield_now();
        }

        let mut app = App::new_for_tests();
        app.running.store(true, Ordering::Release);
        app.worker_handle = Some(handle);

        app.reap_worker();

        assert_eq!(app.status_message.as_deref(), Some(CRASH_MESSAGE));
        assert!(!app.running.load(Ordering::Acquire));
        assert!(!app.worker_active());
    }

    #[test]
    fn platform_copy_changes_with_support_modes() {
        let mut app = App::new_for_tests();

        app.hotkey_support = HotkeySupport::Global;
        app.mouse_capture_support = MouseCaptureSupport::Direct;
        assert_eq!(app.mouse_capture_button_label(), "Pick On Screen");
        assert_eq!(
            app.mouse_capture_hint(),
            "Press F1 to capture the current mouse position, or click Pick On Screen to choose a point visually"
        );
        assert_eq!(app.platform_notice(), None);

        app.hotkey_support = HotkeySupport::FocusedOnly;
        app.mouse_capture_support = MouseCaptureSupport::Picker;
        assert_eq!(app.mouse_capture_button_label(), "Pick On Screen");
        assert!(app.mouse_capture_hint().contains("Pick On Screen"));
        assert!(app.platform_notice().unwrap().contains("Wayland detected"));
    }

    #[test]
    fn opening_picker_waits_for_release_before_capture() {
        let mut app = App::new_for_tests();

        app.open_mouse_capture_picker(ViewportRestoreState::default());

        assert!(app.picker_waiting_for_release());
        assert!(app.mouse_capture_picker.is_some());
    }

    #[test]
    fn picker_stays_blocked_while_primary_button_is_still_down() {
        let mut app = App::new_for_tests();
        app.open_mouse_capture_picker(ViewportRestoreState::default());

        app.arm_mouse_capture_picker_after_release(true);

        assert!(app.picker_waiting_for_release());
    }

    #[test]
    fn picker_arms_after_primary_button_is_released() {
        let mut app = App::new_for_tests();
        app.open_mouse_capture_picker(ViewportRestoreState::default());

        app.arm_mouse_capture_picker_after_release(false);

        assert!(!app.picker_waiting_for_release());
    }

    #[test]
    fn picker_selection_applies_coordinates_and_clears_session() {
        let mut app = App::new_for_tests();
        app.open_mouse_capture_picker(ViewportRestoreState::default());
        app.arm_mouse_capture_picker_after_release(false);
        let restore_state = app.take_mouse_capture_picker_restore_state();

        app.apply_mouse_capture_picker_selection(25, 40);

        assert!(restore_state.is_some());
        assert_eq!(app.mouse_x, "25");
        assert_eq!(app.mouse_y, "40");
        assert!(app.mouse_capture_picker.is_none());
    }
}
