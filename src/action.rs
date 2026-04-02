use std::fmt;

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum MouseButton {
    Left,
    Right,
}

impl fmt::Display for MouseButton {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            MouseButton::Left => write!(f, "Left"),
            MouseButton::Right => write!(f, "Right"),
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub enum Action {
    MouseClick { button: MouseButton, x: i32, y: i32 },
    KeyPress { key: String },
    Delay { ms: u64 },
}

impl fmt::Display for Action {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Action::MouseClick { button, x, y } => {
                write!(f, "{} Click on ({}, {})", button, x, y)
            }
            Action::KeyPress { key } => write!(f, "Press {}", key),
            Action::Delay { ms } => {
                if *ms >= 1000 && *ms % 1000 == 0 {
                    write!(f, "{}s Delay", ms / 1000)
                } else {
                    write!(f, "{}ms Delay", ms)
                }
            }
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub enum StopCondition {
    HotkeyOnly,
    Timer { seconds: u64 },
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mouse_button_display_labels_are_human_readable() {
        assert_eq!(MouseButton::Left.to_string(), "Left");
        assert_eq!(MouseButton::Right.to_string(), "Right");
    }

    #[test]
    fn mouse_click_action_display_includes_button_and_coordinates() {
        assert_eq!(
            Action::MouseClick {
                button: MouseButton::Right,
                x: 120,
                y: 240,
            }
            .to_string(),
            "Right Click on (120, 240)"
        );
    }

    #[test]
    fn key_press_action_display_includes_key_name() {
        assert_eq!(
            Action::KeyPress {
                key: String::from("space"),
            }
            .to_string(),
            "Press space"
        );
    }

    #[test]
    fn delay_action_display_formats_milliseconds_and_seconds() {
        assert_eq!(Action::Delay { ms: 250 }.to_string(), "250ms Delay");
        assert_eq!(Action::Delay { ms: 2000 }.to_string(), "2s Delay");
        assert_eq!(Action::Delay { ms: 2500 }.to_string(), "2500ms Delay");
    }
}
