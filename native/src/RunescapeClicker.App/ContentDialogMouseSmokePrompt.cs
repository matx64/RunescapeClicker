using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public sealed class ContentDialogMouseSmokePrompt : IMouseSmokePrompt
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Func<XamlRoot?> _xamlRootProvider;

    public ContentDialogMouseSmokePrompt(
        DispatcherQueue dispatcherQueue,
        Func<XamlRoot?> xamlRootProvider)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _xamlRootProvider = xamlRootProvider ?? throw new ArgumentNullException(nameof(xamlRootProvider));
    }

    public Task<bool> ConfirmAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_dispatcherQueue.HasThreadAccess)
        {
            return ShowDialogAsync(point, cancellationToken);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var confirmed = await ShowDialogAsync(point, cancellationToken);
                    completion.SetResult(confirmed);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("Failed to show the mouse smoke confirmation dialog."));
        }

        return completion.Task;
    }

    private async Task<bool> ShowDialogAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        var dialog = new ContentDialog
        {
            Title = "Run mouse smoke?",
            Content = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"This opt-in smoke action performs a real right click at ({point.X}, {point.Y}) after a short countdown. Continue only if that target is safe."),
            PrimaryButtonText = "Run Mouse Smoke",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        var xamlRoot = _xamlRootProvider();
        if (xamlRoot is not null)
        {
            dialog.XamlRoot = xamlRoot;
        }

        var result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary;
    }
}
