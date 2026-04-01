use global_hotkey::{
    hotkey::{Code, HotKey, Modifiers},
    GlobalHotKeyEvent, GlobalHotKeyManager, HotKeyState,
};
use std::env;
use std::fmt::{self, Display, Formatter};
use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::Arc;

#[cfg(target_os = "linux")]
const DISABLE_X11_DISPLAY: &str = "__rs_clicker_disable_x11__";
#[cfg(target_os = "linux")]
const DISABLE_WAYLAND_DISPLAY: &str = "__rs_clicker_disable_wayland__";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum HotkeySupport {
    Global,
    FocusedOnly,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MouseCaptureSupport {
    Direct,
    Picker,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum InputBackend {
    #[cfg(not(target_os = "linux"))]
    Default,
    Wayland,
    X11,
}

impl InputBackend {
    fn label(self) -> &'static str {
        match self {
            #[cfg(not(target_os = "linux"))]
            Self::Default => "default",
            Self::Wayland => "wayland",
            Self::X11 => "x11",
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
struct InputBackendAttempt {
    backend: InputBackend,
    settings: enigo::Settings,
}

#[derive(Clone, Debug, PartialEq, Eq)]
struct InputBackendFailure {
    backend: InputBackend,
    reason: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct InputBackendConnectError {
    failures: Vec<InputBackendFailure>,
}

impl Display for InputBackendConnectError {
    fn fmt(&self, f: &mut Formatter<'_>) -> fmt::Result {
        if self.failures.is_empty() {
            return write!(f, "no input backends were configured");
        }

        let attempted_backends = self
            .failures
            .iter()
            .map(|failure| failure.backend)
            .collect::<Vec<_>>();
        let details = self
            .failures
            .iter()
            .map(|failure| format!("{}: {}", failure.backend.label(), failure.reason))
            .collect::<Vec<_>>()
            .join("; ");

        write!(
            f,
            "tried {}. Details: {details}",
            format_backend_sequence(&attempted_backends)
        )?;

        if self
            .failures
            .iter()
            .any(|failure| failure.backend == InputBackend::Wayland)
        {
            write!(
                f,
                " Wayland input injection depends on compositor support, and x11 fallback requires an available XWayland display."
            )?;
        }

        Ok(())
    }
}

fn session_env_var(name: &str) -> Option<String> {
    env::var(name).ok().filter(|value| !value.is_empty())
}

fn is_wayland_session() -> bool {
    let session_type = env::var("XDG_SESSION_TYPE")
        .ok()
        .map(|value| value.to_ascii_lowercase());
    session_env_var("WAYLAND_DISPLAY").is_some()
        || matches!(session_type.as_deref(), Some("wayland"))
}

fn format_backend_sequence(backends: &[InputBackend]) -> String {
    let labels = backends
        .iter()
        .map(|backend| backend.label())
        .collect::<Vec<_>>();
    match labels.as_slice() {
        [] => String::from("no backends"),
        [label] => (*label).to_string(),
        [first, second] => format!("{first}, then {second}"),
        _ => {
            let last = labels.last().copied().unwrap_or("unknown");
            format!("{}, then {last}", labels[..labels.len() - 1].join(", "))
        }
    }
}

#[cfg(target_os = "linux")]
#[derive(Clone, Debug, PartialEq, Eq)]
struct LinuxInputSession {
    is_wayland: bool,
    wayland_display: Option<String>,
    x11_display: Option<String>,
}

#[cfg(target_os = "linux")]
impl LinuxInputSession {
    fn detect() -> Self {
        Self {
            is_wayland: is_wayland_session(),
            wayland_display: session_env_var("WAYLAND_DISPLAY"),
            x11_display: session_env_var("DISPLAY"),
        }
    }
}

#[cfg(target_os = "linux")]
fn wayland_backend_attempt(wayland_display: Option<&str>) -> InputBackendAttempt {
    let mut settings = enigo::Settings::default();
    // Avoid double-injecting events through XWayland when a native Wayland
    // backend is available for this session.
    settings.x11_display = Some(String::from(DISABLE_X11_DISPLAY));
    settings.wayland_display = wayland_display.map(str::to_owned);
    InputBackendAttempt {
        backend: InputBackend::Wayland,
        settings,
    }
}

#[cfg(target_os = "linux")]
fn x11_backend_attempt(x11_display: Option<&str>) -> InputBackendAttempt {
    let mut settings = enigo::Settings::default();
    settings.x11_display = x11_display.map(str::to_owned);
    settings.wayland_display = Some(String::from(DISABLE_WAYLAND_DISPLAY));
    InputBackendAttempt {
        backend: InputBackend::X11,
        settings,
    }
}

#[cfg(target_os = "linux")]
fn linux_input_backend_attempts(session: &LinuxInputSession) -> Vec<InputBackendAttempt> {
    if session.is_wayland {
        let mut attempts = vec![wayland_backend_attempt(session.wayland_display.as_deref())];
        if session.x11_display.is_some() {
            attempts.push(x11_backend_attempt(session.x11_display.as_deref()));
        }
        attempts
    } else {
        vec![x11_backend_attempt(session.x11_display.as_deref())]
    }
}

fn input_backend_attempts() -> Vec<InputBackendAttempt> {
    #[cfg(target_os = "linux")]
    {
        return linux_input_backend_attempts(&LinuxInputSession::detect());
    }

    #[cfg(not(target_os = "linux"))]
    {
        vec![InputBackendAttempt {
            backend: InputBackend::Default,
            settings: enigo::Settings::default(),
        }]
    }
}

pub fn connect_input_backend() -> Result<enigo::Enigo, InputBackendConnectError> {
    let mut failures = Vec::new();

    for attempt in input_backend_attempts() {
        match enigo::Enigo::new(&attempt.settings) {
            Ok(enigo) => return Ok(enigo),
            Err(err) => failures.push(InputBackendFailure {
                backend: attempt.backend,
                reason: err.to_string(),
            }),
        }
    }

    Err(InputBackendConnectError { failures })
}

impl HotkeySupport {
    pub fn detect() -> Self {
        #[cfg(target_os = "linux")]
        {
            if is_wayland_session() {
                return Self::FocusedOnly;
            }
        }

        Self::Global
    }

    pub fn stop_hint(self) -> &'static str {
        match self {
            Self::Global => "seconds OR F2 press",
            Self::FocusedOnly => "seconds OR click STOP / press F2 while focused",
        }
    }

    pub fn notice(self) -> Option<&'static str> {
        match self {
            Self::Global => None,
            Self::FocusedOnly => {
                Some("Wayland detected: F2 stop works only while this window is focused.")
            }
        }
    }
}

impl MouseCaptureSupport {
    pub fn detect() -> Self {
        #[cfg(target_os = "linux")]
        {
            if is_wayland_session() {
                return Self::Picker;
            }
        }

        Self::Direct
    }

    pub fn notice(self) -> Option<&'static str> {
        match self {
            Self::Direct => None,
            Self::Picker => {
                Some(
                    "Wayland detected: mouse-position capture uses a transparent overlay on this window's monitor for windowed or borderless apps.",
                )
            }
        }
    }
}

pub struct HotkeyManager {
    support: HotkeySupport,
    _manager: Option<GlobalHotKeyManager>,
    pub f2_id: Option<u32>,
    pub f1_id: Option<u32>,
}

impl HotkeyManager {
    pub fn new() -> Result<Self, String> {
        let support = HotkeySupport::detect();
        if support == HotkeySupport::FocusedOnly {
            return Ok(Self {
                support,
                _manager: None,
                f2_id: None,
                f1_id: None,
            });
        }

        let manager = GlobalHotKeyManager::new()
            .map_err(|err| format!("Failed to initialize global hotkeys: {err}"))?;

        let f2 = HotKey::new(Some(Modifiers::empty()), Code::F2);
        let f1 = HotKey::new(Some(Modifiers::empty()), Code::F1);

        let f2_id = f2.id();
        let f1_id = f1.id();

        manager
            .register(f2)
            .map_err(|err| format!("Failed to register F2 hotkey: {err}"))?;
        manager
            .register(f1)
            .map_err(|err| format!("Failed to register F1 hotkey: {err}"))?;

        Ok(HotkeyManager {
            support,
            _manager: Some(manager),
            f2_id: Some(f2_id),
            f1_id: Some(f1_id),
        })
    }

    pub fn support(&self) -> HotkeySupport {
        self.support
    }

    pub fn poll(
        &self,
        running: &Arc<AtomicBool>,
        captured_position: &Arc<(AtomicI32, AtomicI32)>,
        position_captured: &Arc<AtomicBool>,
    ) -> Option<String> {
        if self.support != HotkeySupport::Global {
            return None;
        }

        let mut error_message = None;

        while let Ok(event) = GlobalHotKeyEvent::receiver().try_recv() {
            if event.state != HotKeyState::Pressed {
                continue;
            }

            if Some(event.id) == self.f2_id {
                running.store(false, Ordering::Release);
            } else if Some(event.id) == self.f1_id {
                if let Err(err) = capture_mouse_position(captured_position, position_captured) {
                    error_message = Some(err);
                }
            }
        }

        error_message
    }
}

pub fn capture_mouse_position(
    captured_position: &Arc<(AtomicI32, AtomicI32)>,
    position_captured: &Arc<AtomicBool>,
) -> Result<(), String> {
    match connect_input_backend() {
        Ok(enigo) => {
            use enigo::Mouse;
            match enigo.location() {
                Ok(pos) => {
                    captured_position.0.store(pos.0, Ordering::Relaxed);
                    captured_position.1.store(pos.1, Ordering::Relaxed);
                    position_captured.store(true, Ordering::Release);
                    Ok(())
                }
                Err(err) => Err(format!("Failed to capture mouse position: {err}")),
            }
        }
        Err(err) => Err(format!(
            "Failed to access the mouse position backend: {err}"
        )),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn global_hotkeys_are_default_off_linux() {
        #[cfg(not(target_os = "linux"))]
        assert_eq!(HotkeySupport::detect(), HotkeySupport::Global);
    }

    #[test]
    fn focused_only_support_uses_wayland_copy() {
        assert_eq!(
            HotkeySupport::FocusedOnly.stop_hint(),
            "seconds OR click STOP / press F2 while focused"
        );
        assert!(HotkeySupport::FocusedOnly.notice().is_some());
    }

    #[test]
    fn global_support_uses_global_hotkey_copy() {
        assert_eq!(HotkeySupport::Global.stop_hint(), "seconds OR F2 press");
        assert!(HotkeySupport::Global.notice().is_none());
    }

    #[test]
    fn wayland_capture_support_uses_picker_copy() {
        assert_eq!(
            MouseCaptureSupport::Picker.notice(),
            Some(
                "Wayland detected: mouse-position capture uses a transparent overlay on this window's monitor for windowed or borderless apps.",
            )
        );
    }

    #[test]
    fn available_capture_support_has_no_warning() {
        assert_eq!(MouseCaptureSupport::Direct.notice(), None);
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn wayland_session_with_display_tries_wayland_then_x11() {
        let attempts = linux_input_backend_attempts(&LinuxInputSession {
            is_wayland: true,
            wayland_display: Some(String::from("wayland-0")),
            x11_display: Some(String::from(":0")),
        });

        assert_eq!(attempts.len(), 2);
        assert_eq!(attempts[0].backend, InputBackend::Wayland);
        assert_eq!(
            attempts[0].settings.x11_display.as_deref(),
            Some(DISABLE_X11_DISPLAY)
        );
        assert_eq!(
            attempts[0].settings.wayland_display.as_deref(),
            Some("wayland-0")
        );

        assert_eq!(attempts[1].backend, InputBackend::X11);
        assert_eq!(attempts[1].settings.x11_display.as_deref(), Some(":0"));
        assert_eq!(
            attempts[1].settings.wayland_display.as_deref(),
            Some(DISABLE_WAYLAND_DISPLAY)
        );
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn wayland_session_without_display_only_tries_wayland() {
        let attempts = linux_input_backend_attempts(&LinuxInputSession {
            is_wayland: true,
            wayland_display: Some(String::from("wayland-0")),
            x11_display: None,
        });

        assert_eq!(attempts.len(), 1);
        assert_eq!(attempts[0].backend, InputBackend::Wayland);
        assert_eq!(
            attempts[0].settings.x11_display.as_deref(),
            Some(DISABLE_X11_DISPLAY)
        );
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn non_wayland_session_uses_x11_attempt() {
        let attempts = linux_input_backend_attempts(&LinuxInputSession {
            is_wayland: false,
            wayland_display: None,
            x11_display: Some(String::from(":1")),
        });

        assert_eq!(attempts.len(), 1);
        assert_eq!(attempts[0].backend, InputBackend::X11);
        assert_eq!(attempts[0].settings.x11_display.as_deref(), Some(":1"));
        assert_eq!(
            attempts[0].settings.wayland_display.as_deref(),
            Some(DISABLE_WAYLAND_DISPLAY)
        );
    }

    #[test]
    fn input_backend_error_mentions_backends_and_hint() {
        let error = InputBackendConnectError {
            failures: vec![
                InputBackendFailure {
                    backend: InputBackend::Wayland,
                    reason: String::from("no connection could be established"),
                },
                InputBackendFailure {
                    backend: InputBackend::X11,
                    reason: String::from("failed to establish the connection"),
                },
            ],
        };

        let rendered = error.to_string();
        assert!(rendered.contains("tried wayland, then x11"));
        assert!(rendered.contains("wayland: no connection could be established"));
        assert!(rendered.contains("x11: failed to establish the connection"));
        assert!(rendered.contains("Wayland input injection depends on compositor support"));
    }
}
