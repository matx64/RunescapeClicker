use eframe::egui;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum Icon {
    MouseClick,
    Keyboard,
    Delay,
    Clear,
    Start,
    Stop,
    MoveUp,
    MoveDown,
    Remove,
    ActionList,
    Notice,
    Status,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) enum IconSize {
    Toolbar,
    PrimaryAction,
    SectionHeading,
    CompactControl,
    StatusRow,
}

impl IconSize {
    pub(crate) fn points(self) -> f32 {
        match self {
            Self::Toolbar => 22.0,
            Self::PrimaryAction => 22.0,
            Self::SectionHeading => 20.0,
            Self::CompactControl => 18.0,
            Self::StatusRow => 18.0,
        }
    }
}

impl Icon {
    fn source(self) -> egui::ImageSource<'static> {
        match self {
            Self::MouseClick => egui::include_image!("../assets/icons/mouse-pointer-click.svg"),
            Self::Keyboard => egui::include_image!("../assets/icons/keyboard.svg"),
            Self::Delay => egui::include_image!("../assets/icons/clock-3.svg"),
            Self::Clear => egui::include_image!("../assets/icons/trash-2.svg"),
            Self::Start => egui::include_image!("../assets/icons/play.svg"),
            Self::Stop => egui::include_image!("../assets/icons/square.svg"),
            Self::MoveUp => egui::include_image!("../assets/icons/arrow-up.svg"),
            Self::MoveDown => egui::include_image!("../assets/icons/arrow-down.svg"),
            Self::Remove => egui::include_image!("../assets/icons/trash-2.svg"),
            Self::ActionList => egui::include_image!("../assets/icons/list-ordered.svg"),
            Self::Notice => egui::include_image!("../assets/icons/info.svg"),
            Self::Status => egui::include_image!("../assets/icons/triangle-alert.svg"),
        }
    }
}

pub(crate) fn image(icon: Icon, size: IconSize) -> egui::Image<'static> {
    let points = size.points();
    egui::Image::new(icon.source())
        .fit_to_exact_size(egui::vec2(points, points))
        .maintain_aspect_ratio(true)
}

pub(crate) fn tinted(icon: Icon, size: IconSize, color: egui::Color32) -> egui::Image<'static> {
    image(icon, size).tint(color)
}
