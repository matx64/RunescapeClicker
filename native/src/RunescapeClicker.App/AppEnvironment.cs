namespace RunescapeClicker.App;

public static class AppEnvironment
{
    public static string Summary =>
        $"Loaded {typeof(RunescapeClicker.Core.AssemblyMarker).Assembly.GetName().Name}, "
        + $"{typeof(RunescapeClicker.Automation.Windows.AssemblyMarker).Assembly.GetName().Name}, "
        + "with the Phase 5 WinUI shell ready for action editing, native Windows key capture, and run-state validation.";
}
