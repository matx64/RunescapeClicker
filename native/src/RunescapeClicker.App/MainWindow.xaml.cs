using Microsoft.UI.Xaml;

namespace RunescapeClicker.App;

public sealed partial class MainWindow : Window
{
    private readonly AppComposition _composition;

    public MainWindow()
    {
        InitializeComponent();
        _composition = AppComposition.CreateDefault(DispatcherQueue, () => Content.XamlRoot);
        ViewModel = _composition.MainViewModel;
        Title = "Runescape Clicker";
        if (Content is FrameworkElement root)
        {
            root.DataContext = ViewModel;
        }

        Closed += OnClosed;
        Activated += OnActivated;
    }

    public MainViewModel ViewModel { get; }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        await ViewModel.InitializeAsync();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        _composition.Dispose();
    }
}
