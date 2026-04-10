using FluentAssertions;

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
    }
}
