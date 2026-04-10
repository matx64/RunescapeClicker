using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using RunescapeClicker.Automation.Windows;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public sealed class AppComposition : IDisposable
{
    private AppComposition(
        WindowsAutomationServices automationServices,
        MainViewModel mainViewModel)
    {
        AutomationServices = automationServices;
        MainViewModel = mainViewModel;
    }

    public WindowsAutomationServices AutomationServices { get; }

    public MainViewModel MainViewModel { get; }

    public static AppComposition CreateDefault(
        DispatcherQueue dispatcherQueue,
        Func<XamlRoot?> xamlRootProvider)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(xamlRootProvider);

        var automationServices = WindowsAutomationServices.CreateDefault();
        var store = new AppSessionStore();
        var coordinator = new RunCoordinator(
            store,
            new ClickerEngine(automationServices.InputAdapter),
            automationServices.InputAdapter,
            automationServices.HotkeyService,
            automationServices.CoordinatePickerService,
            new DispatcherQueueUiDispatcher(dispatcherQueue),
            new ContentDialogMouseSmokePrompt(dispatcherQueue, xamlRootProvider),
            new SystemAsyncDelayScheduler());

        return new AppComposition(automationServices, new MainViewModel(store, coordinator));
    }

    public void Dispose()
    {
        MainViewModel.Dispose();
        AutomationServices.Dispose();
    }
}
