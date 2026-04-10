using RunescapeClicker.Automation.Windows;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public sealed class Phase3HarnessComposition : IDisposable
{
    private Phase3HarnessComposition(WindowsAutomationServices automationServices, IClickerEngine clickerEngine)
    {
        AutomationServices = automationServices;
        ClickerEngine = clickerEngine;
    }

    public WindowsAutomationServices AutomationServices { get; }

    public IClickerEngine ClickerEngine { get; }

    public static Phase3HarnessComposition CreateDefault()
    {
        var automationServices = WindowsAutomationServices.CreateDefault();
        return new Phase3HarnessComposition(
            automationServices,
            new ClickerEngine(automationServices.InputAdapter));
    }

    public void Dispose() => AutomationServices.Dispose();
}
