namespace RunescapeClicker.Automation.Windows;

public interface ICoordinatePickerService : IDisposable
{
    Task<CoordinatePickerResult> PickCoordinateAsync(CancellationToken cancellationToken);
}
