namespace RunescapeClicker.App;

public static class AppEnvironment
{
    public static string Summary =>
        $"Loaded {typeof(RunescapeClicker.Core.AssemblyMarker).Assembly.GetName().Name}, "
        + $"{typeof(RunescapeClicker.Automation.Windows.AssemblyMarker).Assembly.GetName().Name}, "
        + "and a placeholder WinUI shell for Phase 1.";
}
