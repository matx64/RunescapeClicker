use std::fmt;

#[derive(Clone, Debug, PartialEq)]
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

#[derive(Clone, Debug)]
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
