using FluentAssertions;
using RunescapeClicker.Core;

namespace RunescapeClicker.App.Tests;

public sealed class AppAssemblyTests
{
    [Fact]
    public void AppNamespaceMatchesTheBootstrapShell()
    {
        typeof(RunescapeClicker.App.App).Namespace.Should().Be("RunescapeClicker.App");
    }

    [Fact]
    public void AppSummaryMentionsTheCoreAndWindowsLayers()
    {
        RunescapeClicker.App.AppEnvironment.Summary.Should().Contain("RunescapeClicker.Core");
        RunescapeClicker.App.AppEnvironment.Summary.Should().Contain("RunescapeClicker.Automation.Windows");
        RunescapeClicker.App.AppEnvironment.Summary.Should().Contain("Phase 3 native automation harness");
    }

    [Fact]
    public void HarnessCompositionBuildsTheRealEngineAndWindowsServices()
    {
        using var composition = RunescapeClicker.App.Phase3HarnessComposition.CreateDefault();

        composition.ClickerEngine.Should().NotBeNull();
        composition.AutomationServices.HotkeyService.Should().NotBeNull();
        composition.AutomationServices.CoordinatePickerService.Should().NotBeNull();
        composition.AutomationServices.InputAdapter.Should().NotBeNull();
    }

    [Fact]
    public void SafeSmokeRunUsesF24AndHotkeyOnlyStopping()
    {
        var request = RunescapeClicker.App.SmokeRunFactory.CreateSafeKeyboardRun();

        request.StopCondition.Should().Be(StopCondition.HotkeyOnly);
        request.Actions.OfType<KeyPressAction>().Should().ContainSingle(key => key.DisplayLabel == "F24");
    }
}
