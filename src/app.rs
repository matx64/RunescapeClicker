use crate::action::{Action, MouseButton, StopCondition};
use crate::executor;
use crate::hotkey::{capture_mouse_position, HotkeyManager, HotkeySupport, MouseCaptureSupport};
use crate::icons::{self, Icon, IconSize};
use eframe::egui;
use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::mpsc::{self, Receiver, Sender};
use std::sync::Arc;
use std::thread::JoinHandle;
use std::time::Duration;

const COLOR_MOUSE: egui::Color32 = egui::Color32::from_rgb(55, 114, 255);
const COLOR_KEYBOARD: egui::Color32 = egui::Color32::from_rgb(179, 0, 137);
const COLOR_DELAY: egui::Color32 = egui::Color32::from_rgb(242, 187, 5);
const COLOR_CLEAR: egui::Color32 = egui::Color32::from_rgb(123, 132, 145);
const COLOR_TOOLBAR_FILL: egui::Color32 = egui::Color32::from_rgb(36, 41, 48);
const COLOR_START: egui::Color32 = egui::Color32::from_rgb(0, 133, 72);
const COLOR_STOP: egui::Color32 = egui::Color32::from_rgb(210, 62, 62);
const COLOR_INFO: egui::Color32 = egui::Color32::from_rgb(176, 111, 0);
const COLOR_STATUS: egui::Color32 = egui::Color32::from_rgb(249, 86, 79);
const COLOR_TEXT_DARK: egui::Color32 = egui::Color32::from_rgb(28, 33, 40);
const CRASH_MESSAGE: &str = "Automation thread crashed.";
const TOOLBAR_BUTTON_HEIGHT: f32 = 54.0;
const START_BUTTON_SIZE: egui::Vec2 = egui::vec2(160.0, 54.0);
const TOOLBAR_STACK_BREAKPOINT: f32 = 430.0;
const TOOLBAR_GRID_BREAKPOINT: f32 = 760.0;
const FORM_STACK_BREAKPOINT: f32 = 360.0;
const ACTION_ROW_STACK_BREAKPOINT: f32 = 340.0;
const ACTION_CONTROL_WIDTH: f32 = 112.0;
const ACTION_ICON_BUTTON_SIZE: egui::Vec2 = egui::vec2(32.0, 32.0);
const FORM_SUBMIT_BUTTON_SIZE: egui::Vec2 = egui::vec2(104.0, 40.0);

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum AddingState {
    None,
    MouseClick,
    KeyPress,
    Delay,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum ToolbarLayout {
    Row,
    Grid,
    Stack,
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
    selected_mouse_position: Option<(i32, i32)>,

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
    pub fn new(cc: &eframe::CreationContext<'_>) -> Self {
        egui_extras::install_image_loaders(&cc.egui_ctx);

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
            selected_mouse_position: None,
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
            self.adding = state;
        }
    }

    fn start_mouse_click_flow(&mut self, ctx: &egui::Context) {
        self.selected_mouse_position = None;
        self.adding = AddingState::MouseClick;
        self.start_mouse_capture_picker(ctx);
    }

    fn finish_mouse_click_add(&mut self, x: i32, y: i32) {
        self.actions.push(Action::MouseClick {
            button: self.mouse_button,
            x,
            y,
        });
        self.selected_mouse_position = None;
        self.adding = AddingState::None;
    }

    fn try_add_mouse_click(&mut self) -> bool {
        if let Some((x, y)) = self.selected_mouse_position {
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

    fn clear_actions(&mut self) -> bool {
        if self.actions.is_empty() {
            false
        } else {
            self.actions.clear();
            true
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

    fn action_icon(action: &Action) -> Icon {
        match action {
            Action::MouseClick { .. } => Icon::MouseClick,
            Action::KeyPress { .. } => Icon::Keyboard,
            Action::Delay { .. } => Icon::Delay,
        }
    }

    fn styled_button_fill(
        base_fill: egui::Color32,
        response: &egui::Response,
        enabled: bool,
    ) -> egui::Color32 {
        if !enabled {
            base_fill.gamma_multiply(0.65)
        } else if response.is_pointer_button_down_on() {
            base_fill.gamma_multiply(0.92)
        } else if response.hovered() {
            base_fill.gamma_multiply(1.08)
        } else {
            base_fill
        }
    }

    fn styled_button_stroke(
        base_stroke: egui::Stroke,
        response: &egui::Response,
        enabled: bool,
    ) -> egui::Stroke {
        if !enabled {
            egui::Stroke::new(base_stroke.width, base_stroke.color.gamma_multiply(0.6))
        } else if response.hovered() {
            egui::Stroke::new(base_stroke.width, base_stroke.color.gamma_multiply(1.12))
        } else {
            base_stroke
        }
    }

    fn render_large_icon_button(
        ui: &mut egui::Ui,
        size: egui::Vec2,
        icon: Icon,
        icon_size: IconSize,
        label: &str,
        text_size: f32,
        fill: egui::Color32,
        stroke: egui::Stroke,
        foreground: Option<egui::Color32>,
    ) -> egui::Response {
        let (rect, response) = ui.allocate_exact_size(size, egui::Sense::click());
        response.widget_info(|| {
            egui::WidgetInfo::labeled(egui::WidgetType::Button, ui.is_enabled(), label)
        });

        if ui.is_rect_visible(rect) {
            let visuals = ui.style().interact(&response);
            let enabled = ui.is_enabled();
            let frame_fill = Self::styled_button_fill(fill, &response, enabled);
            let frame_stroke = Self::styled_button_stroke(stroke, &response, enabled);
            let frame_rect = rect.expand2(egui::Vec2::splat(visuals.expansion));
            let inner_rect = rect.shrink2(egui::vec2(18.0, 10.0));
            let text_color = match foreground {
                Some(color) if enabled => color,
                Some(color) => color.gamma_multiply(0.55),
                None => visuals.text_color(),
            };
            let icon_width = icon_size.points();
            let icon_height = icon_width;
            let icon_gap = ui.spacing().icon_spacing.max(8.0);
            let galley = ui.painter().layout_no_wrap(
                label.to_owned(),
                egui::FontId::new(text_size, egui::FontFamily::Proportional),
                text_color,
            );
            let content_width = (icon_width + icon_gap + galley.size().x).min(inner_rect.width());
            let start_x = (inner_rect.center().x - (content_width / 2.0)).max(inner_rect.left());
            let icon_rect = egui::Rect::from_min_size(
                egui::pos2(start_x, inner_rect.center().y - (icon_height / 2.0)),
                egui::vec2(icon_width, icon_height),
            );
            let text_pos = egui::pos2(
                icon_rect.right() + icon_gap,
                inner_rect.center().y - (galley.size().y / 2.0),
            );

            ui.painter().rect(
                frame_rect,
                egui::CornerRadius::same(8),
                frame_fill,
                frame_stroke,
                egui::StrokeKind::Inside,
            );
            icons::tinted(icon, icon_size, text_color).paint_at(ui, icon_rect);
            ui.painter().galley(text_pos, galley, text_color);
        }

        if let Some(cursor) = ui.visuals().interact_cursor {
            if response.hovered() {
                ui.ctx().set_cursor_icon(cursor);
            }
        }

        response
    }

    fn render_toolbar_button(
        ui: &mut egui::Ui,
        width: f32,
        icon: Icon,
        label: &str,
        accent: egui::Color32,
    ) -> egui::Response {
        Self::render_large_icon_button(
            ui,
            egui::vec2(width, TOOLBAR_BUTTON_HEIGHT),
            icon,
            IconSize::Toolbar,
            label,
            16.0,
            COLOR_TOOLBAR_FILL,
            egui::Stroke::new(2.0, accent),
            None,
        )
    }

    fn render_run_button(
        ui: &mut egui::Ui,
        icon: Icon,
        label: &str,
        fill: egui::Color32,
    ) -> egui::Response {
        Self::render_large_icon_button(
            ui,
            START_BUTTON_SIZE,
            icon,
            IconSize::PrimaryAction,
            label,
            18.0,
            fill,
            egui::Stroke::new(1.0, fill.gamma_multiply(0.7)),
            None,
        )
    }

    fn action_control_button(icon: Icon, tint: egui::Color32) -> egui::Button<'static> {
        egui::Button::image(icons::tinted(icon, IconSize::CompactControl, tint))
            .corner_radius(6)
            .min_size(ACTION_ICON_BUTTON_SIZE)
    }

    fn preferred_foreground(fill: egui::Color32) -> egui::Color32 {
        let [r, g, b, _] = fill.to_array();
        let luminance =
            (0.2126 * f32::from(r) + 0.7152 * f32::from(g) + 0.0722 * f32::from(b)) / 255.0;

        if luminance > 0.6 {
            COLOR_TEXT_DARK
        } else {
            egui::Color32::WHITE
        }
    }

    fn render_form_submit_button(
        ui: &mut egui::Ui,
        icon: Icon,
        accent: egui::Color32,
    ) -> egui::Response {
        let foreground = Self::preferred_foreground(accent);
        let stroke_color = if foreground == egui::Color32::WHITE {
            accent.gamma_multiply(0.75)
        } else {
            foreground.gamma_multiply(0.8)
        };

        Self::render_large_icon_button(
            ui,
            FORM_SUBMIT_BUTTON_SIZE,
            icon,
            IconSize::CompactControl,
            "Add",
            16.0,
            accent,
            egui::Stroke::new(1.0, stroke_color),
            Some(foreground),
        )
    }

    fn render_section_heading(ui: &mut egui::Ui, icon: Icon, title: &str, color: egui::Color32) {
        ui.horizontal(|ui| {
            ui.add(icons::tinted(icon, IconSize::SectionHeading, color));
            ui.label(egui::RichText::new(title).strong().size(16.0).color(color));
        });
    }

    fn render_message_row(ui: &mut egui::Ui, icon: Icon, color: egui::Color32, message: &str) {
        ui.horizontal_wrapped(|ui| {
            ui.add(icons::tinted(icon, IconSize::StatusRow, color));
            ui.add(egui::Label::new(egui::RichText::new(message).color(color)).wrap());
        });
    }

    fn worker_active(&self) -> bool {
        self.worker_handle.is_some()
    }

    fn toolbar_layout(available_width: f32) -> ToolbarLayout {
        if available_width < TOOLBAR_STACK_BREAKPOINT {
            ToolbarLayout::Stack
        } else if available_width < TOOLBAR_GRID_BREAKPOINT {
            ToolbarLayout::Grid
        } else {
            ToolbarLayout::Row
        }
    }

    fn form_stacks(available_width: f32) -> bool {
        available_width < FORM_STACK_BREAKPOINT
    }

    fn action_row_stacks(available_width: f32) -> bool {
        available_width < ACTION_ROW_STACK_BREAKPOINT
    }

    fn handle_toolbar_action(&mut self, state: AddingState, ctx: &egui::Context) {
        match state {
            AddingState::MouseClick => self.start_mouse_click_flow(ctx),
            AddingState::KeyPress | AddingState::Delay => self.toggle_adding(state),
            AddingState::None => {
                self.clear_actions();
            }
        }
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

    fn apply_captured_position(&mut self) {
        self.selected_mouse_position = Some((
            self.captured_position.0.load(Ordering::Relaxed),
            self.captured_position.1.load(Ordering::Relaxed),
        ));
    }

    fn can_capture_mouse_position_direct(&self) -> bool {
        self.adding == AddingState::MouseClick
            && self.mouse_capture_picker.is_none()
            && self.mouse_capture_support == MouseCaptureSupport::Direct
    }

    fn mouse_capture_hint(&self) -> &'static str {
        match (self.mouse_capture_support, self.hotkey_support) {
            (MouseCaptureSupport::Direct, HotkeySupport::Global) => {
                "Picker opens first. Press F1 to capture the current mouse position directly instead."
            }
            (MouseCaptureSupport::Direct, HotkeySupport::FocusedOnly) => {
                "Picker opens first. Focus this window and press F1 to capture the current mouse position directly instead."
            }
            (MouseCaptureSupport::Picker, _) => {
                "Picker opens first. On this platform, mouse capture is available only through the transparent overlay."
            }
        }
    }

    fn join_worker(&mut self) {
        if let Some(handle) = self.worker_handle.take() {
            if handle.join().is_err() {
                self.status_message = Some(String::from(CRASH_MESSAGE));
            }
        }

        self.running.store(false, Ordering::Release);
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

    fn capture_mouse_position_direct(&mut self) {
        match capture_mouse_position(&self.captured_position, &self.position_captured) {
            Ok(()) => {
                self.apply_pending_direct_capture();
            }
            Err(err) => {
                self.status_message = Some(err);
            }
        }
    }

    fn apply_pending_direct_capture(&mut self) -> bool {
        if !self.position_captured.swap(false, Ordering::AcqRel) {
            return false;
        }

        if !self.can_capture_mouse_position_direct() {
            return false;
        }

        self.apply_captured_position();
        self.status_message = Some(String::from("Mouse position captured."));
        true
    }

    fn apply_mouse_capture_picker_selection(&mut self, x: i32, y: i32) {
        self.selected_mouse_position = Some((x, y));
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

    fn render_mouse_capture_picker(&mut self, ui: &mut egui::Ui) {
        let ctx = ui.ctx().clone();
        let cancel_requested = ctx.input(|input| {
            input.key_pressed(egui::Key::Escape) || input.viewport().close_requested()
        });
        if cancel_requested {
            self.cancel_mouse_capture_picker(&ctx);
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
            self.complete_mouse_capture_picker(&ctx, pos);
            return;
        }

        let pointer_pos = ctx.input(|input| input.pointer.latest_pos());
        let preview = pointer_pos.map(|pos| Self::picker_pos_to_pixels(&ctx, pos));
        ctx.request_repaint_after(Duration::from_millis(16));

        egui::CentralPanel::default()
            .frame(egui::Frame::NONE)
            .show_inside(ui, |ui| {
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
            .show(&ctx, |ui| {
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

        let capture_requested = self.can_capture_mouse_position_direct()
            && ctx.input_mut(|input| input.consume_key(egui::Modifiers::NONE, egui::Key::F1));
        if capture_requested {
            self.capture_mouse_position_direct();
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
        let actions = [
            (
                Icon::MouseClick,
                "Add Mouse Click",
                COLOR_MOUSE,
                AddingState::MouseClick,
            ),
            (
                Icon::Keyboard,
                "Add Keyboard Press",
                COLOR_KEYBOARD,
                AddingState::KeyPress,
            ),
            (Icon::Delay, "Add Delay", COLOR_DELAY, AddingState::Delay),
            (Icon::Clear, "Clear", COLOR_CLEAR, AddingState::None),
        ];

        ui.add_enabled_ui(!worker_active, |ui| {
            match Self::toolbar_layout(available_width) {
                ToolbarLayout::Stack => {
                    for (icon, label, accent, state) in actions {
                        if ui
                            .scope(|ui| {
                                Self::render_toolbar_button(
                                    ui,
                                    ui.available_width(),
                                    icon,
                                    label,
                                    accent,
                                )
                            })
                            .inner
                            .clicked()
                        {
                            self.handle_toolbar_action(state, ui.ctx());
                        }
                    }
                }
                ToolbarLayout::Grid => {
                    let item_spacing = ui.spacing().item_spacing;
                    let button_width = ((available_width - item_spacing.x) / 2.0).max(0.0);

                    egui::Grid::new("toolbar_grid")
                        .num_columns(2)
                        .min_col_width(button_width)
                        .spacing(item_spacing)
                        .show(ui, |ui| {
                            for (index, (icon, label, accent, state)) in
                                actions.into_iter().enumerate()
                            {
                                if ui
                                    .scope(|ui| {
                                        Self::render_toolbar_button(
                                            ui,
                                            button_width,
                                            icon,
                                            label,
                                            accent,
                                        )
                                    })
                                    .inner
                                    .clicked()
                                {
                                    self.handle_toolbar_action(state, ui.ctx());
                                }

                                if index % 2 == 1 {
                                    ui.end_row();
                                }
                            }
                        });
                }
                ToolbarLayout::Row => {
                    let button_spacing = ui.spacing().item_spacing.x;
                    let button_width = ((available_width - (button_spacing * 3.0)) / 4.0).max(0.0);

                    ui.horizontal(|ui| {
                        for (icon, label, accent, state) in actions {
                            if ui
                                .scope(|ui| {
                                    Self::render_toolbar_button(
                                        ui,
                                        button_width,
                                        icon,
                                        label,
                                        accent,
                                    )
                                })
                                .inner
                                .clicked()
                            {
                                self.handle_toolbar_action(state, ui.ctx());
                            }
                        }
                    });
                }
            }
        });
    }

    fn render_mouse_click_form(&mut self, ui: &mut egui::Ui) {
        let (x_display, y_display) = match self.selected_mouse_position {
            Some((x, y)) => (x.to_string(), y.to_string()),
            None => (
                String::from("Not picked yet"),
                String::from("Not picked yet"),
            ),
        };
        let has_selected_position = self.selected_mouse_position.is_some();

        ui.group(|ui| {
            Self::render_section_heading(ui, Icon::MouseClick, "Mouse Click", COLOR_MOUSE);
            ui.horizontal_wrapped(|ui| {
                ui.radio_value(&mut self.mouse_button, MouseButton::Left, "Left");
                ui.radio_value(&mut self.mouse_button, MouseButton::Right, "Right");
            });

            if Self::form_stacks(ui.available_width()) {
                ui.label("X:");
                ui.monospace(&x_display);
                ui.label("Y:");
                ui.monospace(&y_display);
            } else {
                let field_width =
                    ((ui.available_width() - ui.spacing().item_spacing.x) / 2.0).max(120.0);

                ui.horizontal(|ui| {
                    ui.vertical(|ui| {
                        ui.label("X:");
                        ui.add_sized(
                            [field_width, 0.0],
                            egui::Label::new(egui::RichText::new(&x_display).monospace()),
                        );
                    });
                    ui.vertical(|ui| {
                        ui.label("Y:");
                        ui.add_sized(
                            [field_width, 0.0],
                            egui::Label::new(egui::RichText::new(&y_display).monospace()),
                        );
                    });
                });
            }

            ui.label(
                egui::RichText::new(
                    "Add Mouse Click opens a transparent overlay on this window's monitor. Move this app onto the target monitor first.",
                )
                .color(COLOR_INFO),
            );
            ui.label(self.mouse_capture_hint());

            if ui
                .add_enabled_ui(has_selected_position, |ui| {
                    Self::render_form_submit_button(ui, Icon::MouseClick, COLOR_MOUSE)
                })
                .inner
                .clicked()
            {
                self.try_add_mouse_click();
            }
        });
    }

    fn render_key_press_form(&mut self, ui: &mut egui::Ui) {
        ui.group(|ui| {
            Self::render_section_heading(ui, Icon::Keyboard, "Key Press", COLOR_KEYBOARD);
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

            if Self::render_form_submit_button(ui, Icon::Keyboard, COLOR_KEYBOARD).clicked() {
                self.try_add_key_press();
            }
        });
    }

    fn render_delay_form(&mut self, ui: &mut egui::Ui) {
        ui.group(|ui| {
            Self::render_section_heading(ui, Icon::Delay, "Delay", COLOR_DELAY);

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

            if Self::render_form_submit_button(ui, Icon::Delay, COLOR_DELAY).clicked() {
                self.try_add_delay();
            }
        });
    }

    fn render_action_list(&mut self, ui: &mut egui::Ui, worker_active: bool) {
        Self::render_section_heading(ui, Icon::ActionList, "Loop Order", egui::Color32::WHITE);

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
                    let action_icon = Self::action_icon(action);

                    ui.group(|ui| {
                        if Self::action_row_stacks(ui.available_width()) {
                            ui.horizontal_wrapped(|ui| {
                                ui.add(icons::tinted(action_icon, IconSize::CompactControl, color));
                                ui.add(egui::Label::new(label.clone()).wrap());
                            });

                            if !worker_active {
                                ui.horizontal_wrapped(|ui| {
                                    if i > 0
                                        && ui
                                            .add(Self::action_control_button(
                                                Icon::MoveUp,
                                                egui::Color32::WHITE,
                                            ))
                                            .on_hover_text("Move up")
                                            .clicked()
                                    {
                                        to_move = Some((i, -1));
                                    }
                                    if i < self.actions.len() - 1
                                        && ui
                                            .add(Self::action_control_button(
                                                Icon::MoveDown,
                                                egui::Color32::WHITE,
                                            ))
                                            .on_hover_text("Move down")
                                            .clicked()
                                    {
                                        to_move = Some((i, 1));
                                    }
                                    if ui
                                        .add(Self::action_control_button(Icon::Remove, COLOR_STOP))
                                        .on_hover_text("Remove action")
                                        .clicked()
                                    {
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
                                ui.allocate_ui_with_layout(
                                    egui::vec2(label_width, 0.0),
                                    egui::Layout::left_to_right(egui::Align::Center),
                                    |ui| {
                                        ui.add(icons::tinted(
                                            action_icon,
                                            IconSize::CompactControl,
                                            color,
                                        ));
                                        ui.add(egui::Label::new(label.clone()).wrap());
                                    },
                                );

                                if !worker_active {
                                    ui.with_layout(
                                        egui::Layout::right_to_left(egui::Align::Center),
                                        |ui| {
                                            if ui
                                                .add(Self::action_control_button(
                                                    Icon::Remove,
                                                    COLOR_STOP,
                                                ))
                                                .on_hover_text("Remove action")
                                                .clicked()
                                            {
                                                to_remove = Some(i);
                                            }
                                            if i < self.actions.len() - 1
                                                && ui
                                                    .add(Self::action_control_button(
                                                        Icon::MoveDown,
                                                        egui::Color32::WHITE,
                                                    ))
                                                    .on_hover_text("Move down")
                                                    .clicked()
                                            {
                                                to_move = Some((i, 1));
                                            }
                                            if i > 0
                                                && ui
                                                    .add(Self::action_control_button(
                                                        Icon::MoveUp,
                                                        egui::Color32::WHITE,
                                                    ))
                                                    .on_hover_text("Move up")
                                                    .clicked()
                                            {
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
            Self::render_section_heading(ui, Icon::Delay, "Stop Condition", COLOR_DELAY);
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

    fn logic(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        // Poll hotkeys
        let direct_capture_enabled = self.can_capture_mouse_position_direct();
        if let Some(hotkey_manager) = &self.hotkey_manager {
            if let Some(message) = hotkey_manager.poll(
                &self.running,
                direct_capture_enabled,
                &self.captured_position,
                &self.position_captured,
            ) {
                self.status_message = Some(message);
            }
        }

        self.poll_status_messages();
        self.reap_worker();
        self.handle_focused_hotkeys(ctx, self.worker_active());
        self.apply_pending_direct_capture();

        // Request repaint to keep polling hotkeys
        ctx.request_repaint_after(Duration::from_millis(100));
    }

    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        if self.mouse_capture_picker.is_some() {
            self.render_mouse_capture_picker(ui);
            return;
        }

        let worker_active = self.worker_active();
        let is_running = self.running.load(Ordering::Acquire);
        let is_stopping = worker_active && !is_running;
        egui::CentralPanel::default().show_inside(ui, |ui| {
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
                        Self::render_message_row(ui, Icon::Notice, COLOR_INFO, notice);
                    }

                    if let Some(message) = &self.status_message {
                        ui.separator();
                        Self::render_message_row(ui, Icon::Status, COLOR_STATUS, message);
                    }

                    ui.separator();

                    // === Start/Stop Button ===
                    ui.vertical_centered(|ui| {
                        if is_running {
                            if Self::render_run_button(ui, Icon::Stop, "STOP", COLOR_STOP).clicked()
                            {
                                self.stop_worker();
                            }
                        } else if is_stopping {
                            ui.add_enabled_ui(false, |ui| {
                                Self::render_run_button(ui, Icon::Stop, "Stopping...", COLOR_STOP);
                            });
                        } else if !self.actions.is_empty()
                            && Self::render_run_button(ui, Icon::Start, "START", COLOR_START)
                                .clicked()
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
    fn starting_mouse_click_flow_clears_saved_position_and_opens_picker() {
        let mut app = App::new_for_tests();
        let ctx = egui::Context::default();
        app.selected_mouse_position = Some((120, 340));

        app.start_mouse_click_flow(&ctx);

        assert_eq!(app.adding, AddingState::MouseClick);
        assert_eq!(app.selected_mouse_position, None);
        assert!(app.mouse_capture_picker.is_some());
    }

    #[test]
    fn adding_mouse_click_clears_selected_position_and_closes_form() {
        let mut app = App::new_for_tests();
        app.mouse_button = MouseButton::Right;
        app.adding = AddingState::MouseClick;
        app.selected_mouse_position = Some((45, 90));

        assert!(app.try_add_mouse_click());
        assert_eq!(app.adding, AddingState::None);
        assert_eq!(app.selected_mouse_position, None);
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
    fn non_mouse_forms_do_not_reset_selected_position() {
        let mut app = App::new_for_tests();
        app.selected_mouse_position = Some((12, 34));

        app.toggle_adding(AddingState::KeyPress);
        assert_eq!(app.adding, AddingState::KeyPress);
        assert_eq!(app.selected_mouse_position, Some((12, 34)));

        app.toggle_adding(AddingState::Delay);
        assert_eq!(app.adding, AddingState::Delay);
        assert_eq!(app.selected_mouse_position, Some((12, 34)));
    }

    #[test]
    fn mouse_click_without_selected_position_leaves_form_open() {
        let mut app = App::new_for_tests();
        app.adding = AddingState::MouseClick;

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
    fn clear_actions_empties_populated_action_list() {
        let mut app = App::new_for_tests();
        app.actions = vec![
            Action::Delay { ms: 100 },
            Action::KeyPress {
                key: String::from("space"),
            },
        ];

        assert!(app.clear_actions());
        assert!(app.actions.is_empty());
    }

    #[test]
    fn clear_actions_is_no_op_when_list_is_empty() {
        let mut app = App::new_for_tests();

        assert!(!app.clear_actions());
        assert!(app.actions.is_empty());
    }

    #[test]
    fn clear_actions_preserves_other_editor_state() {
        let mut app = App::new_for_tests();
        app.actions.push(Action::Delay { ms: 100 });
        app.adding = AddingState::KeyPress;
        app.key_input = String::from("space");
        app.delay_ms = String::from("250");
        app.selected_mouse_position = Some((12, 34));
        app.stop_condition = StopCondition::Timer { seconds: 25 };
        app.status_message = Some(String::from("ready"));

        assert!(app.clear_actions());
        assert!(app.actions.is_empty());
        assert_eq!(app.adding, AddingState::KeyPress);
        assert_eq!(app.key_input, "space");
        assert_eq!(app.delay_ms, "250");
        assert_eq!(app.selected_mouse_position, Some((12, 34)));
        assert_eq!(app.stop_condition, StopCondition::Timer { seconds: 25 });
        assert_eq!(app.status_message.as_deref(), Some("ready"));
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
    fn platform_notice_changes_with_support_modes() {
        let mut app = App::new_for_tests();

        app.hotkey_support = HotkeySupport::Global;
        app.mouse_capture_support = MouseCaptureSupport::Direct;
        assert_eq!(app.platform_notice(), None);
        assert!(app.mouse_capture_hint().contains("Press F1"));

        app.hotkey_support = HotkeySupport::FocusedOnly;
        app.mouse_capture_support = MouseCaptureSupport::Picker;
        assert!(app.platform_notice().unwrap().contains("Wayland detected"));
        assert!(app
            .mouse_capture_hint()
            .contains("only through the transparent overlay"));
    }

    #[test]
    fn direct_capture_hotkey_requires_mouse_click_form_without_picker() {
        let mut app = App::new_for_tests();

        assert!(!app.can_capture_mouse_position_direct());

        app.adding = AddingState::MouseClick;
        assert!(app.can_capture_mouse_position_direct());

        app.open_mouse_capture_picker(ViewportRestoreState::default());
        assert!(!app.can_capture_mouse_position_direct());

        app.mouse_capture_picker = None;
        app.mouse_capture_support = MouseCaptureSupport::Picker;
        assert!(!app.can_capture_mouse_position_direct());
    }

    #[test]
    fn applying_pending_direct_capture_updates_selected_position_and_status() {
        let mut app = App::new_for_tests();
        app.adding = AddingState::MouseClick;
        app.captured_position.0.store(320, Ordering::Relaxed);
        app.captured_position.1.store(640, Ordering::Relaxed);
        app.position_captured.store(true, Ordering::Release);

        assert!(app.apply_pending_direct_capture());
        assert_eq!(app.selected_mouse_position, Some((320, 640)));
        assert_eq!(
            app.status_message.as_deref(),
            Some("Mouse position captured.")
        );
    }

    #[test]
    fn applying_pending_direct_capture_is_ignored_while_picker_is_active() {
        let mut app = App::new_for_tests();
        app.adding = AddingState::MouseClick;
        app.open_mouse_capture_picker(ViewportRestoreState::default());
        app.captured_position.0.store(100, Ordering::Relaxed);
        app.captured_position.1.store(200, Ordering::Relaxed);
        app.position_captured.store(true, Ordering::Release);

        assert!(!app.apply_pending_direct_capture());
        assert_eq!(app.selected_mouse_position, None);
        assert_eq!(app.position_captured.load(Ordering::Acquire), false);
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
        assert_eq!(app.selected_mouse_position, Some((25, 40)));
        assert!(app.mouse_capture_picker.is_none());
    }

    #[test]
    fn cancelling_picker_keeps_mouse_click_form_open() {
        let mut app = App::new_for_tests();
        let ctx = egui::Context::default();
        app.adding = AddingState::MouseClick;
        app.selected_mouse_position = Some((25, 40));
        app.open_mouse_capture_picker(ViewportRestoreState::default());

        app.cancel_mouse_capture_picker(&ctx);

        assert_eq!(app.adding, AddingState::MouseClick);
        assert_eq!(app.selected_mouse_position, Some((25, 40)));
        assert!(app.mouse_capture_picker.is_none());
        assert_eq!(
            app.status_message.as_deref(),
            Some("Mouse position capture cancelled.")
        );
    }
}
