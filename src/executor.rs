use crate::action::{Action, MouseButton, StopCondition};
use crate::hotkey::connect_input_backend;
use enigo::{Button, Coordinate, Direction, Key, Keyboard, Mouse};
use rand::Rng;
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

pub trait InputDriver {
    fn move_mouse_abs(&mut self, x: i32, y: i32) -> Result<(), String>;
    fn click_mouse(&mut self, button: MouseButton) -> Result<(), String>;
    fn press_key(&mut self, key: &str) -> Result<(), String>;
}

pub trait ExecutionRuntime {
    type Input: InputDriver;

    fn connect_input(&mut self) -> Result<Self::Input, String>;
    fn anti_detect_delay_ms(&mut self) -> u64;
    fn delay_jitter_ms(&mut self) -> u64;
    fn elapsed(&self) -> Duration;
    fn sleep(&mut self, duration: Duration, running: &AtomicBool, stop: &StopCondition) -> bool;
}

struct EnigoInputDriver {
    enigo: enigo::Enigo,
}

impl InputDriver for EnigoInputDriver {
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

                    if let Err(err) = input.move_mouse_abs(*x, *y) {
                        report_fatal_error(
                            running,
                            status_tx,
                            format!("Failed to move the mouse: {err}"),
                        );
                        break 'sequence;
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
