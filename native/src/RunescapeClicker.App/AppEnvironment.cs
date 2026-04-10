namespace RunescapeClicker.App;

public static class AppEnvironment
{
    public static string Summary =>
        $"Loaded {typeof(RunescapeClicker.Core.AssemblyMarker).Assembly.GetName().Name}, "
        + $"{typeof(RunescapeClicker.Automation.Windows.AssemblyMarker).Assembly.GetName().Name}, "
        + "with the Phase 3 native automation harness ready for hotkeys, coordinate capture, and manual smoke validation.";
}
