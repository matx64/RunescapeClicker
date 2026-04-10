namespace RunescapeClicker.App;

public static class AppEnvironment
{
    public static string Summary =>
        $"Loaded {typeof(RunescapeClicker.Core.AssemblyMarker).Assembly.GetName().Name}, "
        + $"{typeof(RunescapeClicker.Automation.Windows.AssemblyMarker).Assembly.GetName().Name}, "
        + "with the Phase 4 application layer ready for session state, hotkeys, coordinate capture, and manual smoke validation.";
}
