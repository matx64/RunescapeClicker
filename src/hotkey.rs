use global_hotkey::{
    hotkey::{Code, HotKey, Modifiers},
    GlobalHotKeyEvent, GlobalHotKeyManager, HotKeyState,
};
use std::env;
use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::Arc;

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

fn is_wayland_session() -> bool {
    let session_type = env::var("XDG_SESSION_TYPE")
        .ok()
        .map(|value| value.to_ascii_lowercase());
    env::var_os("WAYLAND_DISPLAY").is_some() || matches!(session_type.as_deref(), Some("wayland"))
}

pub fn input_backend_settings() -> enigo::Settings {
    let mut settings = enigo::Settings::default();

    #[cfg(target_os = "linux")]
    {
        if is_wayland_session() {
            // Avoid double-injecting events through XWayland when a native Wayland
            // backend is available for this session.
            settings.x11_display = Some(String::from("__rs_clicker_disable_x11__"));
            settings.wayland_display = env::var("WAYLAND_DISPLAY").ok();
        } else {
            settings.wayland_display = Some(String::from("__rs_clicker_disable_wayland__"));
        }
    }

    settings
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
    match enigo::Enigo::new(&input_backend_settings()) {
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
}
