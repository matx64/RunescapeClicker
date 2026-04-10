use runescape_clicker::action::{Action, MouseButton, StopCondition};
use runescape_clicker::executor::{execute_sequence_with_runtime, ExecutionRuntime, InputDriver};
use std::collections::VecDeque;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{mpsc, Arc, Mutex};
use std::time::Duration;

#[derive(Default)]
struct FakeDriverState {
    operations: Vec<String>,
    current_location: (i32, i32),
    location_error: Option<String>,
    move_error: Option<String>,
    click_error: Option<String>,
    key_error: Option<String>,
}

struct FakeInputDriver {
    state: Arc<Mutex<FakeDriverState>>,
}

impl InputDriver for FakeInputDriver {
    fn mouse_location(&self) -> Result<(i32, i32), String> {
        let mut state = self.state.lock().unwrap();
        if let Some(err) = state.location_error.take() {
            return Err(err);
        }

        Ok(state.current_location)
    }

    fn move_mouse_abs(&mut self, x: i32, y: i32) -> Result<(), String> {
        let mut state = self.state.lock().unwrap();
        if let Some(err) = state.move_error.take() {
            return Err(err);
        }

        state.current_location = (x, y);
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
    movement_curve_factors: VecDeque<f64>,
    post_move_click_delays: VecDeque<u64>,
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
            movement_curve_factors: VecDeque::new(),
            post_move_click_delays: VecDeque::new(),
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

    fn movement_curve_factor(&mut self) -> f64 {
        self.movement_curve_factors.pop_front().unwrap_or_default()
    }

    fn post_move_click_delay_ms(&mut self) -> u64 {
        self.post_move_click_delays.pop_front().unwrap_or_default()
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
    runtime.delay_jitters = VecDeque::from([0]);
    runtime.movement_curve_factors = VecDeque::from([0.35]);
    runtime.post_move_click_delays = VecDeque::from([32]);

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
            Action::Delay { ms: 900 },
        ],
        &StopCondition::Timer { seconds: 1 },
        &running,
        &status_tx,
        &mut runtime,
    );

    let operations = runtime.operations();
    assert!(operations.len() > 3);
    assert_eq!(operations.last().unwrap(), "key:space");
    assert_eq!(operations[operations.len() - 2], "click:left");
    assert_eq!(operations[operations.len() - 3], "move:10:20");
    assert!(operations[..operations.len() - 2]
        .iter()
        .all(|op| op.starts_with("move:")));

    let sleep_log = runtime.sleep_log();
    assert!(sleep_log.len() > 4);
    assert_eq!(sleep_log.first().copied(), Some(Duration::from_millis(25)));
    assert!(sleep_log.contains(&Duration::from_millis(32)));
    assert_eq!(sleep_log.last().copied(), Some(Duration::from_millis(900)));
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
    runtime.driver_state.lock().unwrap().current_location = (100, 100);
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
    runtime.movement_curve_factors = VecDeque::from([0.0]);
    runtime.post_move_click_delays = VecDeque::from([22]);
    runtime.driver_state.lock().unwrap().current_location = (90, 120);
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
    let operations = runtime.operations();
    assert!(operations.len() > 1);
    assert_eq!(operations.last().unwrap(), "move:3:7");
    assert!(operations.iter().all(|op| op.starts_with("move:")));
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

#[test]
fn mouse_click_uses_interpolated_movement_before_click() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([15]);
    runtime.delay_jitters = VecDeque::from([0]);
    runtime.movement_curve_factors = VecDeque::from([0.25]);
    runtime.post_move_click_delays = VecDeque::from([30]);
    runtime.driver_state.lock().unwrap().current_location = (0, 0);

    execute_sequence_with_runtime(
        &[
            Action::MouseClick {
                button: MouseButton::Left,
                x: 120,
                y: 80,
            },
            Action::Delay { ms: 900 },
        ],
        &StopCondition::Timer { seconds: 1 },
        &running,
        &status_tx,
        &mut runtime,
    );

    let operations = runtime.operations();
    assert!(operations.len() > 2);
    assert_eq!(operations.last().unwrap(), "click:left");
    assert_eq!(operations[operations.len() - 2], "move:120:80");
    assert!(operations[..operations.len() - 1]
        .iter()
        .all(|op| op.starts_with("move:")));

    let sleep_log = runtime.sleep_log();
    assert!(sleep_log.len() > 3);
    assert_eq!(sleep_log.first().copied(), Some(Duration::from_millis(15)));
    assert!(sleep_log.contains(&Duration::from_millis(30)));
    assert_eq!(sleep_log.last().copied(), Some(Duration::from_millis(900)));
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}

#[test]
fn interpolated_mouse_movement_stays_within_start_target_bounds() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([10]);
    runtime.movement_curve_factors = VecDeque::from([1.0]);
    runtime.post_move_click_delays = VecDeque::from([25]);
    runtime.driver_state.lock().unwrap().current_location = (3, 200);

    execute_sequence_with_runtime(
        &[
            Action::MouseClick {
                button: MouseButton::Left,
                x: 3,
                y: 500,
            },
            Action::Delay { ms: 1000 },
        ],
        &StopCondition::Timer { seconds: 1 },
        &running,
        &status_tx,
        &mut runtime,
    );

    let operations = runtime.operations();
    assert!(operations.len() > 1);
    assert_eq!(operations.last().unwrap(), "click:left");
    assert!(operations[..operations.len() - 1]
        .iter()
        .all(|op| op.starts_with("move:3:")));
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}

#[test]
fn stop_during_mouse_movement_aborts_before_click() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([10]);
    runtime.movement_curve_factors = VecDeque::from([0.0]);
    runtime.post_move_click_delays = VecDeque::from([28]);
    runtime.driver_state.lock().unwrap().current_location = (0, 0);
    runtime.stop_on_sleep_call = Some(2);

    execute_sequence_with_runtime(
        &[Action::MouseClick {
            button: MouseButton::Right,
            x: 200,
            y: 50,
        }],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    let operations = runtime.operations();
    assert!(!operations.is_empty());
    assert!(operations.iter().all(|op| op.starts_with("move:")));
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}

#[test]
fn mouse_location_failure_falls_back_to_single_move_and_click() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([10]);
    runtime.delay_jitters = VecDeque::from([0]);
    runtime.post_move_click_delays = VecDeque::from([27]);
    runtime.driver_state.lock().unwrap().location_error = Some(String::from("unavailable"));

    execute_sequence_with_runtime(
        &[
            Action::MouseClick {
                button: MouseButton::Right,
                x: 50,
                y: 60,
            },
            Action::Delay { ms: 1000 },
        ],
        &StopCondition::Timer { seconds: 1 },
        &running,
        &status_tx,
        &mut runtime,
    );

    assert_eq!(
        runtime.operations(),
        vec![String::from("move:50:60"), String::from("click:right"),]
    );
    assert_eq!(
        runtime.sleep_log(),
        vec![
            Duration::from_millis(10),
            Duration::from_millis(27),
            Duration::from_millis(1000)
        ]
    );
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}

#[test]
fn clicking_same_position_waits_before_click() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([10]);
    runtime.delay_jitters = VecDeque::from([0]);
    runtime.post_move_click_delays = VecDeque::from([31]);
    runtime.driver_state.lock().unwrap().current_location = (50, 60);

    execute_sequence_with_runtime(
        &[
            Action::MouseClick {
                button: MouseButton::Left,
                x: 50,
                y: 60,
            },
            Action::Delay { ms: 1000 },
        ],
        &StopCondition::Timer { seconds: 1 },
        &running,
        &status_tx,
        &mut runtime,
    );

    assert_eq!(runtime.operations(), vec![String::from("click:left")]);
    assert_eq!(
        runtime.sleep_log(),
        vec![
            Duration::from_millis(10),
            Duration::from_millis(31),
            Duration::from_millis(1000)
        ]
    );
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}

#[test]
fn stop_during_post_move_click_delay_aborts_before_click() {
    let running = AtomicBool::new(true);
    let (status_tx, status_rx) = mpsc::channel();
    let mut runtime = FakeRuntime::new();
    runtime.anti_detect_delays = VecDeque::from([10]);
    runtime.delay_jitters = VecDeque::from([0]);
    runtime.post_move_click_delays = VecDeque::from([35]);
    runtime.driver_state.lock().unwrap().current_location = (50, 60);
    runtime.stop_on_sleep_call = Some(2);

    execute_sequence_with_runtime(
        &[Action::MouseClick {
            button: MouseButton::Left,
            x: 50,
            y: 60,
        }],
        &StopCondition::HotkeyOnly,
        &running,
        &status_tx,
        &mut runtime,
    );

    assert!(runtime.operations().is_empty());
    assert_eq!(
        runtime.sleep_log(),
        vec![Duration::from_millis(10), Duration::from_millis(35)]
    );
    assert!(!running.load(Ordering::Acquire));
    assert!(status_rx.try_recv().is_err());
}
