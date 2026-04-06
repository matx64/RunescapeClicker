use crate::action::{Action, MouseButton, StopCondition};
use crate::hotkey::connect_input_backend;
use enigo::{Button, Coordinate, Direction, Key, Keyboard, Mouse};
use rand::Rng;
use std::f64::consts::PI;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::Sender;
use std::sync::Arc;
use std::thread;
use std::time::{Duration, Instant};

/// Minimum random delay before each input action (ms)
const ANTI_DETECT_MIN_MS: u64 = 20;
/// Maximum random delay before each input action (ms)
const ANTI_DETECT_MAX_MS: u64 = 80;
/// Maximum random jitter added to explicit delay actions (ms)
const DELAY_JITTER_MAX_MS: u64 = 50;
/// Poll interval for interruptible sleeps (ms)
const SLEEP_POLL_MS: u64 = 10;
/// Minimum total duration for human-like mouse movement (ms)
const HUMAN_MOVE_MIN_MS: u64 = 120;
/// Maximum total duration for human-like mouse movement (ms)
const HUMAN_MOVE_MAX_MS: u64 = 340;
/// Minimum interpolation steps for human-like mouse movement
const HUMAN_MOVE_MIN_STEPS: u32 = 14;
/// Maximum interpolation steps for human-like mouse movement
const HUMAN_MOVE_MAX_STEPS: u32 = 36;
/// Additional movement duration per pixel of travel
const HUMAN_MOVE_MS_PER_PIXEL: f64 = 0.22;
/// Maximum orthogonal drift applied to human-like mouse movement (px)
const HUMAN_MOVE_MAX_DRIFT_PX: f64 = 8.0;
/// Ratio of total travel distance allowed for orthogonal drift
const HUMAN_MOVE_DRIFT_RATIO: f64 = 0.015;
/// Minimum random delay after reaching the target and before clicking (ms)
const POST_MOVE_CLICK_DELAY_MIN_MS: u64 = 22;
/// Maximum random delay after reaching the target and before clicking (ms)
const POST_MOVE_CLICK_DELAY_MAX_MS: u64 = 38;

pub trait InputDriver {
    fn mouse_location(&self) -> Result<(i32, i32), String>;
    fn move_mouse_abs(&mut self, x: i32, y: i32) -> Result<(), String>;
    fn click_mouse(&mut self, button: MouseButton) -> Result<(), String>;
    fn press_key(&mut self, key: &str) -> Result<(), String>;
}

pub trait ExecutionRuntime {
    type Input: InputDriver;

    fn connect_input(&mut self) -> Result<Self::Input, String>;
    fn anti_detect_delay_ms(&mut self) -> u64;
    fn delay_jitter_ms(&mut self) -> u64;
    fn movement_curve_factor(&mut self) -> f64;
    fn post_move_click_delay_ms(&mut self) -> u64;
    fn elapsed(&self) -> Duration;
    fn sleep(&mut self, duration: Duration, running: &AtomicBool, stop: &StopCondition) -> bool;
}

struct EnigoInputDriver {
    enigo: enigo::Enigo,
}

impl InputDriver for EnigoInputDriver {
    fn mouse_location(&self) -> Result<(i32, i32), String> {
        self.enigo.location().map_err(|err| err.to_string())
    }

    fn move_mouse_abs(&mut self, x: i32, y: i32) -> Result<(), String> {
        self.enigo
            .move_mouse(x, y, Coordinate::Abs)
            .map_err(|err| err.to_string())
    }

    fn click_mouse(&mut self, button: MouseButton) -> Result<(), String> {
        let button = match button {
            MouseButton::Left => Button::Left,
            MouseButton::Right => Button::Right,
        };

        self.enigo
            .button(button, Direction::Click)
            .map_err(|err| err.to_string())
    }

    fn press_key(&mut self, key: &str) -> Result<(), String> {
        self.enigo
            .key(string_to_key(key), Direction::Click)
            .map_err(|err| err.to_string())
    }
}

struct RealRuntime {
    rng: rand::rngs::ThreadRng,
    start: Instant,
}

impl RealRuntime {
    fn new() -> Self {
        Self {
            rng: rand::thread_rng(),
            start: Instant::now(),
        }
    }
}

impl ExecutionRuntime for RealRuntime {
    type Input = EnigoInputDriver;

    fn connect_input(&mut self) -> Result<Self::Input, String> {
        connect_input_backend()
            .map(|enigo| EnigoInputDriver { enigo })
            .map_err(|err| err.to_string())
    }

    fn anti_detect_delay_ms(&mut self) -> u64 {
        self.rng.gen_range(ANTI_DETECT_MIN_MS..=ANTI_DETECT_MAX_MS)
    }

    fn delay_jitter_ms(&mut self) -> u64 {
        self.rng.gen_range(0..=DELAY_JITTER_MAX_MS)
    }

    fn movement_curve_factor(&mut self) -> f64 {
        self.rng.gen_range(-1.0..=1.0)
    }

    fn post_move_click_delay_ms(&mut self) -> u64 {
        self.rng
            .gen_range(POST_MOVE_CLICK_DELAY_MIN_MS..=POST_MOVE_CLICK_DELAY_MAX_MS)
    }

    fn elapsed(&self) -> Duration {
        self.start.elapsed()
    }

    fn sleep(&mut self, duration: Duration, running: &AtomicBool, stop: &StopCondition) -> bool {
        cooperative_sleep(duration, running, stop, &self.start)
    }
}

fn stop_requested(running: &AtomicBool, stop: &StopCondition, elapsed: Duration) -> bool {
    if !running.load(Ordering::Acquire) {
        return true;
    }

    if let StopCondition::Timer { seconds } = stop {
        if elapsed >= Duration::from_secs(*seconds) {
            running.store(false, Ordering::Release);
            return true;
        }
    }

    false
}

fn cooperative_sleep(
    duration: Duration,
    running: &AtomicBool,
    stop: &StopCondition,
    start: &Instant,
) -> bool {
    let deadline = Instant::now() + duration;

    while Instant::now() < deadline {
        if stop_requested(running, stop, start.elapsed()) {
            return false;
        }

        let remaining = deadline.saturating_duration_since(Instant::now());
        thread::sleep(remaining.min(Duration::from_millis(SLEEP_POLL_MS)));
    }

    true
}

fn anti_detect_sleep(
    runtime: &mut impl ExecutionRuntime,
    running: &AtomicBool,
    stop: &StopCondition,
) -> bool {
    let delay = runtime.anti_detect_delay_ms();
    runtime.sleep(Duration::from_millis(delay), running, stop)
}

fn ease_in_out(t: f64) -> f64 {
    0.5 - 0.5 * (PI * t).cos()
}

fn movement_duration_ms(distance: f64) -> u64 {
    let unclamped = HUMAN_MOVE_MIN_MS as f64 + (distance * HUMAN_MOVE_MS_PER_PIXEL);
    unclamped
        .round()
        .clamp(HUMAN_MOVE_MIN_MS as f64, HUMAN_MOVE_MAX_MS as f64) as u64
}

fn movement_steps(distance: f64) -> u32 {
    let unclamped = HUMAN_MOVE_MIN_STEPS as f64 + (distance / 25.0);
    unclamped
        .round()
        .clamp(HUMAN_MOVE_MIN_STEPS as f64, HUMAN_MOVE_MAX_STEPS as f64) as u32
}

fn movement_step_sleep(total_duration: Duration, step_index: u32, step_count: u32) -> Duration {
    if step_count <= 1 {
        return Duration::ZERO;
    }

    let total_nanos = total_duration.as_nanos();
    let sleep_count = (step_count - 1) as u128;
    let base_nanos = total_nanos / sleep_count;
    let remainder = total_nanos % sleep_count;
    let extra_nanos = u128::from(step_index - 1 < remainder as u32);

    Duration::from_nanos((base_nanos + extra_nanos) as u64)
}

fn post_move_click_delay(
    runtime: &mut impl ExecutionRuntime,
    running: &AtomicBool,
    stop: &StopCondition,
) -> bool {
    let delay = runtime.post_move_click_delay_ms();
    runtime.sleep(Duration::from_millis(delay), running, stop)
}

fn move_mouse_human_like(
    input: &mut impl InputDriver,
    runtime: &mut impl ExecutionRuntime,
    target_x: i32,
    target_y: i32,
    running: &AtomicBool,
    stop: &StopCondition,
) -> Result<bool, String> {
    let (start_x, start_y) = match input.mouse_location() {
        Ok(position) => position,
        Err(_) => {
            input.move_mouse_abs(target_x, target_y)?;
            return Ok(post_move_click_delay(runtime, running, stop));
        }
    };

    let dx = (target_x - start_x) as f64;
    let dy = (target_y - start_y) as f64;
    let distance = dx.hypot(dy);

    if distance == 0.0 {
        return Ok(post_move_click_delay(runtime, running, stop));
    }

    let total_duration = Duration::from_millis(movement_duration_ms(distance));
    let step_count = movement_steps(distance);
    let drift_cap = HUMAN_MOVE_MAX_DRIFT_PX.min(distance * HUMAN_MOVE_DRIFT_RATIO);
    let drift_magnitude = drift_cap * runtime.movement_curve_factor();
    let normal_x = -dy / distance;
    let normal_y = dx / distance;
    let min_x = start_x.min(target_x) as f64;
    let max_x = start_x.max(target_x) as f64;
    let min_y = start_y.min(target_y) as f64;
    let max_y = start_y.max(target_y) as f64;
    let mut previous = (start_x, start_y);

    for step in 1..=step_count {
        let t = step as f64 / step_count as f64;
        let eased_t = ease_in_out(t);
        let taper = (PI * t).sin();
        let drift = drift_magnitude * taper;

        let next = if step == step_count {
            (target_x, target_y)
        } else {
            let raw_x = start_x as f64 + (dx * eased_t) + (normal_x * drift);
            let raw_y = start_y as f64 + (dy * eased_t) + (normal_y * drift);
            (
                raw_x.clamp(min_x, max_x).round() as i32,
                raw_y.clamp(min_y, max_y).round() as i32,
            )
        };

        if next != previous {
            input.move_mouse_abs(next.0, next.1)?;
            previous = next;
        }

        if step < step_count {
            let step_sleep = movement_step_sleep(total_duration, step, step_count);
            if !runtime.sleep(step_sleep, running, stop) {
                return Ok(false);
            }
        }
    }

    Ok(post_move_click_delay(runtime, running, stop))
}

fn report_fatal_error(running: &AtomicBool, status_tx: &Sender<String>, message: String) {
    running.store(false, Ordering::Release);
    let _ = status_tx.send(message);
}

pub fn execute_sequence_with_runtime<R: ExecutionRuntime>(
    actions: &[Action],
    stop: &StopCondition,
    running: &AtomicBool,
    status_tx: &Sender<String>,
    runtime: &mut R,
) {
    if actions.is_empty() {
        running.store(false, Ordering::Release);
        return;
    }

    let mut input = match runtime.connect_input() {
        Ok(input) => input,
        Err(err) => {
            report_fatal_error(
                running,
                status_tx,
                format!("Failed to start the input backend: {err}"),
            );
            return;
        }
    };

    'sequence: loop {
        if stop_requested(running, stop, runtime.elapsed()) {
            break;
        }

        for action in actions {
            if stop_requested(running, stop, runtime.elapsed()) {
                break;
            }

            match action {
                Action::MouseClick { button, x, y } => {
                    if !anti_detect_sleep(runtime, running, stop) {
                        break 'sequence;
                    }

                    match move_mouse_human_like(&mut input, runtime, *x, *y, running, stop) {
                        Ok(true) => {}
                        Ok(false) => break 'sequence,
                        Err(err) => {
                            report_fatal_error(
                                running,
                                status_tx,
                                format!("Failed to move the mouse: {err}"),
                            );
                            break 'sequence;
                        }
                    }

                    if let Err(err) = input.click_mouse(*button) {
                        report_fatal_error(
                            running,
                            status_tx,
                            format!("Failed to click the mouse: {err}"),
                        );
                        break 'sequence;
                    }
                }
                Action::KeyPress { key } => {
                    if !anti_detect_sleep(runtime, running, stop) {
                        break 'sequence;
                    }

                    if let Err(err) = input.press_key(key) {
                        report_fatal_error(
                            running,
                            status_tx,
                            format!("Failed to press the key '{key}': {err}"),
                        );
                        break 'sequence;
                    }
                }
                Action::Delay { ms } => {
                    let total = ms.saturating_add(runtime.delay_jitter_ms());
                    if !runtime.sleep(Duration::from_millis(total), running, stop) {
                        break 'sequence;
                    }
                }
            }
        }
    }

    running.store(false, Ordering::Release);
}

pub fn run_sequence(
    actions: Vec<Action>,
    stop: StopCondition,
    running: Arc<AtomicBool>,
    status_tx: Sender<String>,
) -> thread::JoinHandle<()> {
    thread::spawn(move || {
        let mut runtime = RealRuntime::new();
        execute_sequence_with_runtime(&actions, &stop, running.as_ref(), &status_tx, &mut runtime);
    })
}

fn string_to_key(key: &str) -> Key {
    match key {
        "space" => Key::Space,
        "enter" => Key::Return,
        "tab" => Key::Tab,
        "escape" | "esc" => Key::Escape,
        "backspace" => Key::Backspace,
        "delete" => Key::Delete,
        "up" => Key::UpArrow,
        "down" => Key::DownArrow,
        "left" => Key::LeftArrow,
        "right" => Key::RightArrow,
        "shift" => Key::Shift,
        "ctrl" | "control" => Key::Control,
        "alt" => Key::Alt,
        "f1" => Key::F1,
        "f2" => Key::F2,
        "f3" => Key::F3,
        "f4" => Key::F4,
        "f5" => Key::F5,
        "f6" => Key::F6,
        "f7" => Key::F7,
        "f8" => Key::F8,
        "f9" => Key::F9,
        "f10" => Key::F10,
        "f11" => Key::F11,
        "f12" => Key::F12,
        s if s.len() == 1 => Key::Unicode(s.chars().next().unwrap()),
        _ => Key::Unicode(key.chars().next().unwrap_or('a')),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn timer_stop_flips_running_flag() {
        let running = AtomicBool::new(true);

        assert!(stop_requested(
            &running,
            &StopCondition::Timer { seconds: 0 },
            Duration::ZERO,
        ));
        assert!(!running.load(Ordering::Acquire));
    }

    #[test]
    fn cooperative_sleep_exits_immediately_when_stopped() {
        let running = AtomicBool::new(false);
        let start = Instant::now();

        assert!(!cooperative_sleep(
            Duration::from_millis(20),
            &running,
            &StopCondition::HotkeyOnly,
            &start,
        ));
    }

    #[test]
    fn string_to_key_maps_named_keys() {
        assert_eq!(string_to_key("space"), Key::Space);
        assert_eq!(string_to_key("enter"), Key::Return);
        assert_eq!(string_to_key("left"), Key::LeftArrow);
        assert_eq!(string_to_key("f12"), Key::F12);
    }

    #[test]
    fn string_to_key_maps_single_character_and_falls_back_to_first_character() {
        assert_eq!(string_to_key("x"), Key::Unicode('x'));
        assert_eq!(string_to_key("hello"), Key::Unicode('h'));
        assert_eq!(string_to_key(""), Key::Unicode('a'));
    }
}
