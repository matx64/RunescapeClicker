using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;

namespace RunescapeClicker.App;

public sealed class StatusViewModel : ObservableObject
{
    private readonly AppSessionStore _store;

    public StatusViewModel(AppSessionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _store.PropertyChanged += (_, args) => OnStorePropertyChanged(args.PropertyName);
    }

    public string HotkeyStatusText => _store.HotkeyStatusText;

    public string LastHotkeyText => _store.LastHotkeyText;

    public string PickedCoordinateText => _store.PickedCoordinateText;

    public string RunStatusText => _store.RunStatusText;

    public string StatusMessage => _store.StatusMessage;

    public InfoBarSeverity StatusSeverity => _store.StatusSeverity;

    public string LogText => _store.LogText;

    public bool IsStatusOpen => !string.IsNullOrWhiteSpace(_store.StatusMessage);

    private void OnStorePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(AppSessionStore.HotkeyStatusText):
                OnPropertyChanged(nameof(HotkeyStatusText));
                break;
            case nameof(AppSessionStore.LastHotkeyText):
                OnPropertyChanged(nameof(LastHotkeyText));
                break;
            case nameof(AppSessionStore.SelectedCoordinate):
                OnPropertyChanged(nameof(PickedCoordinateText));
                break;
            case nameof(AppSessionStore.RunStatusText):
                OnPropertyChanged(nameof(RunStatusText));
                break;
            case nameof(AppSessionStore.StatusMessage):
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(IsStatusOpen));
                break;
            case nameof(AppSessionStore.StatusSeverity):
                OnPropertyChanged(nameof(StatusSeverity));
                break;
            case nameof(AppSessionStore.LogText):
                OnPropertyChanged(nameof(LogText));
                break;
        }
    }
}
