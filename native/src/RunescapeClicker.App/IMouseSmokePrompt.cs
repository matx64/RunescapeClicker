using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public interface IMouseSmokePrompt
{
    Task<bool> ConfirmAsync(ScreenPoint point, CancellationToken cancellationToken);
}
