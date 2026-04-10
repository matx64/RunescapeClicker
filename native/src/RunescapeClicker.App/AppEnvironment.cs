namespace RunescapeClicker.App;

public static class AppEnvironment
{
    public static string Summary =>
        $"Loaded {typeof(RunescapeClicker.Core.AssemblyMarker).Assembly.GetName().Name}, "
        + $"{typeof(RunescapeClicker.Automation.Windows.AssemblyMarker).Assembly.GetName().Name}, "
        + "with the Phase 6 WinUI shell ready for action editing, friendly Windows failure guidance, and self-contained packaging.";
}
