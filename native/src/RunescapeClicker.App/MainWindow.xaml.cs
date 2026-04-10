using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

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
            root.KeyDown += OnShellKeyDown;
            UpdateAdaptiveLayout(root.ActualWidth);
        }

        Closed += OnClosed;
        Activated += OnActivated;
        SizeChanged += OnWindowSizeChanged;
    }

    public MainViewModel ViewModel { get; }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        await ViewModel.InitializeAsync();
    }

    private void OnShellKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (ViewModel.ActionComposer.TryCaptureKey(args.Key))
        {
            args.Handled = true;
        }
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs args)
        => UpdateAdaptiveLayout(args.Size.Width);

    private void OnSequenceDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        => ViewModel.ActionList.CommitCurrentItemOrder();

    private void UpdateAdaptiveLayout(double width)
    {
        var useWideLayout = width >= 1100;
        LeftPaneColumn.Width = new GridLength(1, GridUnitType.Star);
        RightPaneColumn.Width = useWideLayout ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(RightPaneStack, useWideLayout ? 1 : 0);
        Grid.SetRow(RightPaneStack, useWideLayout ? 0 : 1);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        _composition.Dispose();
    }
}
