use runescape_clicker::app::App;

fn main() -> eframe::Result {
    let options = eframe::NativeOptions {
        viewport: eframe::egui::ViewportBuilder::default()
            .with_inner_size([500.0, 600.0])
            .with_min_inner_size([400.0, 400.0])
            .with_transparent(true),
        ..Default::default()
    };

    eframe::run_native(
        "Runescape Clicker",
        options,
        Box::new(|cc| Ok(Box::new(App::new(cc)))),
    )
}
