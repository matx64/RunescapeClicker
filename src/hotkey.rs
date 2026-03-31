use global_hotkey::{
    hotkey::{Code, HotKey, Modifiers},
    GlobalHotKeyEvent, GlobalHotKeyManager, HotKeyState,
};
use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::Arc;

pub struct HotkeyManager {
    _manager: GlobalHotKeyManager,
    pub f2_id: u32,
    pub f1_id: u32,
}

impl HotkeyManager {
    pub fn new() -> Result<Self, String> {
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
            _manager: manager,
            f2_id,
            f1_id,
        })
    }

    pub fn poll(
        &self,
        running: &Arc<AtomicBool>,
        captured_position: &Arc<(AtomicI32, AtomicI32)>,
        position_captured: &Arc<AtomicBool>,
    ) -> Option<String> {
        let mut error_message = None;

        while let Ok(event) = GlobalHotKeyEvent::receiver().try_recv() {
            if event.state != HotKeyState::Pressed {
                continue;
            }

            if event.id == self.f2_id {
                running.store(false, Ordering::Release);
            } else if event.id == self.f1_id {
                match enigo::Enigo::new(&enigo::Settings::default()) {
                    Ok(enigo) => {
                        use enigo::Mouse;
                        match enigo.location() {
                            Ok(pos) => {
                                captured_position.0.store(pos.0, Ordering::Relaxed);
                                captured_position.1.store(pos.1, Ordering::Relaxed);
                                position_captured.store(true, Ordering::Release);
                            }
                            Err(err) => {
                                error_message =
                                    Some(format!("Failed to capture mouse position: {err}"));
                            }
                        }
                    }
                    Err(err) => {
                        error_message = Some(format!(
                            "Failed to access the mouse position backend: {err}"
                        ));
                    }
                }
            }
        }

        error_message
    }
}
