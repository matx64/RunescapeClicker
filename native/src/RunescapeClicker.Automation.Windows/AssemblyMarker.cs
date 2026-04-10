namespace RunescapeClicker.Automation.Windows;

public static class AssemblyMarker
{
    public static string CoreAssemblyName =>
        typeof(RunescapeClicker.Core.AssemblyMarker).Assembly.GetName().Name
        ?? nameof(RunescapeClicker.Core);
}
