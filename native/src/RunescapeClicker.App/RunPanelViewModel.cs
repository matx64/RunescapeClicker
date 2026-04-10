using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RunescapeClicker.App;

public sealed class RunPanelViewModel : ObservableObject
{
    private readonly AppSessionStore _store;
    private readonly RunCoordinator _coordinator;
    private readonly AsyncRelayCommand _startRunCommand;
    private readonly RelayCommand _stopRunCommand;
    private readonly AsyncRelayCommand _startSafeSmokeCommand;
    private readonly AsyncRelayCommand _runMouseSmokeCommand;

    public RunPanelViewModel(AppSessionStore store, RunCoordinator coordinator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        _startRunCommand = new AsyncRelayCommand(() => _coordinator.StartRunFromSessionAsync(), CanStartRun);
        _stopRunCommand = new RelayCommand(() => _coordinator.RequestStop("Stop requested from the harness."), CanStopRun);
        _startSafeSmokeCommand = new AsyncRelayCommand(() => _coordinator.StartSafeSmokeAsync(), CanStartSmoke);
        _runMouseSmokeCommand = new AsyncRelayCommand(() => _coordinator.StartMouseSmokeAsync(), CanStartMouseSmoke);

        _store.PropertyChanged += (_, args) => OnStorePropertyChanged(args.PropertyName);
        _store.Actions.CollectionChanged += (_, _) => NotifyCommandStates();
    }

    public IReadOnlyList<StopRuleMode> StopModeOptions { get; } = [StopRuleMode.HotkeyOnly, StopRuleMode.Timer];

    public IAsyncRelayCommand StartRunCommand => _startRunCommand;

    public IRelayCommand StopRunCommand => _stopRunCommand;

    public IAsyncRelayCommand StartSafeSmokeCommand => _startSafeSmokeCommand;

    public IAsyncRelayCommand RunMouseSmokeCommand => _runMouseSmokeCommand;

    public StopRuleMode SelectedStopMode
    {
        get => _store.StopRuleMode;
        set
        {
            if (_store.StopRuleMode != value)
            {
                _store.SetStopRuleMode(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTimerStopEnabled));
            }
        }
    }

    public string TimerSecondsText
    {
        get => _store.TimerSecondsText;
        set
        {
            if (_store.TimerSecondsText != value)
            {
                _store.TryUpdateTimerSeconds(value);
                OnPropertyChanged();
            }
        }
    }

    public int StopModeIndex
    {
        get => _store.StopRuleMode == StopRuleMode.Timer ? 1 : 0;
        set
        {
            SelectedStopMode = value == 1 ? StopRuleMode.Timer : StopRuleMode.HotkeyOnly;
            OnPropertyChanged();
        }
    }

    public bool IsTimerStopEnabled => _store.IsTimerStopEnabled;

    public bool IsBusy => _store.RunInProgress;

    private bool CanStartRun() => !_store.RunInProgress && !_store.StopRequested && _store.HasActions;

    private bool CanStopRun() => _store.RunInProgress;

    private bool CanStartSmoke() => !_store.RunInProgress && !_store.StopRequested;

    private bool CanStartMouseSmoke()
        => !_store.RunInProgress && !_store.StopRequested && _store.HasSelectedCoordinate;

    private void OnStorePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(AppSessionStore.StopRuleMode):
                OnPropertyChanged(nameof(SelectedStopMode));
                OnPropertyChanged(nameof(StopModeIndex));
                OnPropertyChanged(nameof(IsTimerStopEnabled));
                break;
            case nameof(AppSessionStore.TimerSecondsText):
                OnPropertyChanged(nameof(TimerSecondsText));
                break;
            case nameof(AppSessionStore.RunInProgress):
                OnPropertyChanged(nameof(IsBusy));
                NotifyCommandStates();
                break;
            case nameof(AppSessionStore.StopRequested):
            case nameof(AppSessionStore.SelectedCoordinate):
                NotifyCommandStates();
                break;
        }
    }

    private void NotifyCommandStates()
    {
        _startRunCommand.NotifyCanExecuteChanged();
        _stopRunCommand.NotifyCanExecuteChanged();
        _startSafeSmokeCommand.NotifyCanExecuteChanged();
        _runMouseSmokeCommand.NotifyCanExecuteChanged();
    }
}
