using Microsoft.UI.Xaml;

namespace RunescapeClicker.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Runescape Clicker";
        StatusText.Text = AppEnvironment.Summary;
    }
}
