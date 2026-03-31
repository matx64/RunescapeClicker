use crate::action::{Action, MouseButton, StopCondition};
use enigo::{Button, Direction, Enigo, Key, Keyboard, Mouse, Settings};
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

fn stop_requested(running: &AtomicBool, stop: &StopCondition, start: &Instant) -> bool {
    if !running.load(Ordering::Acquire) {
        return true;
    }

    if let StopCondition::Timer { seconds } = stop {
        if start.elapsed() >= Duration::from_secs(*seconds) {
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
        if stop_requested(running, stop, start) {
            return false;
        }

        let remaining = deadline.saturating_duration_since(Instant::now());
        thread::sleep(remaining.min(Duration::from_millis(SLEEP_POLL_MS)));
    }

    true
}

fn anti_detect_sleep(
    rng: &mut impl Rng,
    running: &AtomicBool,
    stop: &StopCondition,
    start: &Instant,
) -> bool {
    let delay = rng.gen_range(ANTI_DETECT_MIN_MS..=ANTI_DETECT_MAX_MS);
    cooperative_sleep(Duration::from_millis(delay), running, stop, start)
}

fn report_fatal_error(running: &AtomicBool, status_tx: &Sender<String>, message: String) {
    running.store(false, Ordering::Release);
    let _ = status_tx.send(message);
}

pub fn run_sequence(
    actions: Vec<Action>,
    stop: StopCondition,
    running: Arc<AtomicBool>,
    status_tx: Sender<String>,
) -> thread::JoinHandle<()> {
    thread::spawn(move || {
        let mut enigo = match Enigo::new(&Settings::default()) {
            Ok(enigo) => enigo,
            Err(err) => {
                report_fatal_error(
                    running.as_ref(),
                    &status_tx,
                    format!("Failed to start the input backend: {err}"),
                );
                return;
            }
        };
        let mut rng = rand::thread_rng();
        let start = Instant::now();

        'sequence: loop {
            if stop_requested(running.as_ref(), &stop, &start) {
                break;
            }

            for action in &actions {
                if stop_requested(running.as_ref(), &stop, &start) {
                    break;
                }

                match action {
                    Action::MouseClick { button, x, y } => {
                        if !anti_detect_sleep(&mut rng, running.as_ref(), &stop, &start) {
                            break 'sequence;
                        }

                        if let Err(e) = enigo.move_mouse(*x, *y, enigo::Coordinate::Abs) {
                            report_fatal_error(
                                running.as_ref(),
                                &status_tx,
                                format!("Failed to move the mouse: {e}"),
                            );
                            break 'sequence;
                        }

                        let btn = match button {
                            MouseButton::Left => Button::Left,
                            MouseButton::Right => Button::Right,
                        };

                        if let Err(e) = enigo.button(btn, Direction::Click) {
                            report_fatal_error(
                                running.as_ref(),
                                &status_tx,
                                format!("Failed to click the mouse: {e}"),
                            );
                            break 'sequence;
                        }
                    }
                    Action::KeyPress { key } => {
                        if !anti_detect_sleep(&mut rng, running.as_ref(), &stop, &start) {
                            break 'sequence;
                        }

                        let enigo_key = string_to_key(key);
                        if let Err(e) = enigo.key(enigo_key, Direction::Click) {
                            report_fatal_error(
                                running.as_ref(),
                                &status_tx,
                                format!("Failed to press the key '{key}': {e}"),
                            );
                            break 'sequence;
                        }
                    }
                    Action::Delay { ms } => {
                        let jitter = rng.gen_range(0..=DELAY_JITTER_MAX_MS);
                        let total = ms.saturating_add(jitter);
                        if !cooperative_sleep(
                            Duration::from_millis(total),
                            running.as_ref(),
                            &stop,
                            &start,
                        ) {
                            break 'sequence;
                        }
                    }
                }
            }
        }

        running.store(false, Ordering::Release);
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
        let start = Instant::now();

        assert!(stop_requested(
            &running,
            &StopCondition::Timer { seconds: 0 },
            &start,
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
}
