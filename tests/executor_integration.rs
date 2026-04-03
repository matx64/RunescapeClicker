use runescape_clicker::action::{Action, MouseButton, StopCondition};
use runescape_clicker::executor::{execute_sequence_with_runtime, ExecutionRuntime, InputDriver};
use std::collections::VecDeque;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{mpsc, Arc, Mutex};
use std::time::Duration;

#[derive(Default)]
struct FakeDriverState {
    operations: Vec<String>,
    move_error: Option<String>,
    click_error: Option<String>,
    key_error: Option<String>,
}

struct FakeInputDriver {
    state: Arc<Mutex<FakeDriverState>>,
}

impl InputDriver for FakeInputDriver {
    fn move_mouse_abs(&mut self, x: i32, y: i32) -> Result<(), String> {
        let mut state = self.state.lock().unwrap();
        if let Some(err) = state.move_error.take() {
            return Err(err);
        }

        state.operations.push(format!("move:{x}:{y}"));
        Ok(())
    }

    fn click_mouse(&mut self, button: MouseButton) -> Result<(), String> {
        let mut state = self.state.lock().unwrap();
        if let Some(err) = state.click_error.take() {
            return Err(err);
        }

        state
            .operations
            .push(format!("click:{}", button.to_string().to_ascii_lowercase()));
        Ok(())
    }

    fn press_key(&mut self, key: &str) -> Result<(), String> {
        let mut state = self.state.lock().unwrap();
        if let Some(err) = state.key_error.take() {
            return Err(err);
        }

        state.operations.push(format!("key:{key}"));
        Ok(())
    }
}

struct FakeRuntime {
    driver_state: Arc<Mutex<FakeDriverState>>,
    connect_error: Option<String>,
    anti_detect_delays: VecDeque<u64>,
    delay_jitters: VecDeque<u64>,
    elapsed: Duration,
    sleep_log: Arc<Mutex<Vec<Duration>>>,
    stop_on_sleep_call: Option<usize>,
    sleep_calls: usize,
}

impl FakeRuntime {
    fn new() -> Self {
        Self {
            driver_state: Arc::new(Mutex::new(FakeDriverState::default())),
            connect_error: None,
            anti_detect_delays: VecDeque::new(),
            delay_jitters: VecDeque::new(),
            elapsed: Duration::ZERO,
            sleep_log: Arc::new(Mutex::new(Vec::new())),
            stop_on_sleep_call: None,
            sleep_calls: 0,
        }
    }

    fn operations(&self) -> Vec<String> {
        self.driver_state.lock().unwrap().operations.clone()
    }

    fn sleep_log(&self) -> Vec<Duration> {
        self.sleep_log.lock().unwrap().clone()
    }
}

impl ExecutionRuntime for FakeRuntime {
    type Input = FakeInputDriver;

    fn connect_input(&mut self) -> Result<Self::Input, String> {
        if let Some(err) = self.connect_error.take() {
            return Err(err);
        }

        Ok(FakeInputDriver {
            state: Arc::clone(&self.driver_state),
        })
    }

    fn anti_detect_delay_ms(&mut self) -> u64 {
        self.anti_detect_delays.pop_front().unwrap_or_default()
    }

    fn delay_jitter_ms(&mut self) -> u64 {
        self.delay_jitters.pop_front().unwrap_or_default()
    }

    fn elapsed(&self) -> Duration {
        self.elapsed
    }

    fn sleep(&mut self, duration: Duration, running: &AtomicBool, stop: &StopCondition) -> bool {
        if !running.load(Ordering::Acquire) {
            return false;
        }

        self.sleep_calls += 1;
        self.sleep_log.lock().unwrap().push(duration);

        if Some(self.sleep_calls) == self.stop_on_sleep_call {
            running.store(false, Ordering::Release);
            return false;
        }

        self.elapsed += duration;
        if let StopCondition::Timer { seconds } = stop {
            if self.elapsed >= Duration::from_secs(*seconds) {
                running.store(false, Ordering::Release);
                return false;
            }
        }

        true
    }
}

#[test]
fn successful_sequence_records_expected_operations_and_delays() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([25, 30]);
    runtime.delay_jitters = VecDeque::from([40]);
    runtime.stop_on_sleep_call = Some(3);

    execute_sequence_with_runtime(
        &[
            Action::MouseClick {
                button: MouseButton::Left,
                x: 10,
                y: 20,
            },
            Action::KeyPress {
                key: String::from("space"),
            },
            Action::Delay { ms: 200 },
        ],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    assert_eq!(
        runtime.operations(),
        vec![
            String::from("move:10:20"),
            String::from("click:left"),
            String::from("key:space"),
        ]
    );
    assert_eq!(
        runtime.sleep_log(),
        vec![
            Duration::from_millis(25),
            Duration::from_millis(30),
            Duration::from_millis(240),
        ]
    );
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}

#[test]
fn backend_connect_failure_reports_status_and_stops_running() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.connect_error = Some(String::from("backend unavailable"));

    execute_sequence_with_runtime(
        &[Action::Delay { ms: 50 }],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    assert_eq!(
        status_rx.try_recv().unwrap(),
        "Failed to start the input backend: backend unavailable"
    );
    assert!(!running.load(Ordering::Acquire));
    assert!(runtime.operations().is_empty());
}

#[test]
fn mouse_move_failure_is_reported() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([10]);
    runtime.driver_state.lock().unwrap().move_error = Some(String::from("cursor locked"));

    execute_sequence_with_runtime(
        &[Action::MouseClick {
            button: MouseButton::Right,
            x: 12,
            y: 18,
        }],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    assert_eq!(
        status_rx.try_recv().unwrap(),
        "Failed to move the mouse: cursor locked"
    );
    assert!(!running.load(Ordering::Acquire));
    assert!(runtime.operations().is_empty());
}

#[test]
fn mouse_click_failure_is_reported_after_move() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([10]);
    runtime.driver_state.lock().unwrap().click_error = Some(String::from("button jammed"));

    execute_sequence_with_runtime(
        &[Action::MouseClick {
            button: MouseButton::Left,
            x: 3,
            y: 7,
        }],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    assert_eq!(
        status_rx.try_recv().unwrap(),
        "Failed to click the mouse: button jammed"
    );
    assert_eq!(runtime.operations(), vec![String::from("move:3:7")]);
    assert!(!running.load(Ordering::Acquire));
}

#[test]
fn key_press_failure_is_reported() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([5]);
    runtime.driver_state.lock().unwrap().key_error = Some(String::from("key blocked"));

    execute_sequence_with_runtime(
        &[Action::KeyPress {
            key: String::from("enter"),
        }],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    assert_eq!(
        status_rx.try_recv().unwrap(),
        "Failed to press the key 'enter': key blocked"
    );
    assert!(!running.load(Ordering::Acquire));
    assert!(runtime.operations().is_empty());
}

#[test]
fn cleared_running_flag_stops_before_next_action() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([10, 10]);
    runtime.stop_on_sleep_call = Some(2);

    execute_sequence_with_runtime(
        &[
            Action::KeyPress {
                key: String::from("space"),
            },
            Action::KeyPress {
                key: String::from("enter"),
            },
        ],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    assert_eq!(runtime.operations(), vec![String::from("key:space")]);
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}

#[test]
fn timer_stop_ends_during_delay_before_later_actions() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.delay_jitters = VecDeque::from([0]);

    execute_sequence_with_runtime(
        &[
            Action::Delay { ms: 1200 },
            Action::KeyPress {
                key: String::from("space"),
            },
        ],
        &StopCondition::Timer { seconds: 1 },
        &running,
        &status_tx,
        &mut runtime,
    );

    assert!(runtime.operations().is_empty());
    assert_eq!(runtime.sleep_log(), vec![Duration::from_millis(1200)]);
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}

#[test]
fn empty_action_list_exits_immediately() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();

    execute_sequence_with_runtime(
        &[],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    assert!(runtime.operations().is_empty());
    assert!(runtime.sleep_log().is_empty());
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}
