using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RunescapeClicker.Automation.Windows;

namespace RunescapeClicker.App;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSessionStore _store;
    private readonly RunCoordinator _coordinator;
    private bool _disposed;

    public MainViewModel(
        AppSessionStore store,
        RunCoordinator coordinator,
        IKeyboardKeyMetadataService? keyboardKeyMetadataService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        SummaryText = AppEnvironment.Summary;
        ActionComposer = new ActionComposerViewModel(
            _store,
            _coordinator,
            keyboardKeyMetadataService ?? new KeyboardKeyMetadataService());
        ActionList = new ActionListViewModel(_store, ActionComposer);
        RunPanel = new RunPanelViewModel(_store, _coordinator);
        Status = new StatusViewModel(_store);
        RetryHotkeysCommand = new AsyncRelayCommand(() => _coordinator.InitializeAsync());

        _store.PropertyChanged += (_, args) => OnStorePropertyChanged(args.PropertyName);
        _store.Actions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CurrentState));
        _store.SetStopRuleMode(StopRuleMode.HotkeyOnly);
    }

    public string SummaryText { get; }

    public ActionComposerViewModel ActionComposer { get; }

    public ActionListViewModel ActionList { get; }

    public RunPanelViewModel RunPanel { get; }

    public StatusViewModel Status { get; }

    public IAsyncRelayCommand RetryHotkeysCommand { get; }

    public AppState CurrentState
    {
        get
        {
            if (_store.RunInProgress)
            {
                return _store.StopRequested ? AppState.Stopping : AppState.Running;
            }

            if (_store.PickerActive)
            {
                return AppState.CapturingCoordinate;
            }

            if (_store.ComposerMode != ComposerMode.None)
            {
                return AppState.EditingAction;
            }

            if (_store.LastFault is not null)
            {
                return AppState.Faulted;
            }

            return _store.HasActions ? AppState.ReadyToRun : AppState.Idle;
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => _coordinator.InitializeAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.Dispose();
    }

    private void OnStorePropertyChanged(string? propertyName)
    {
        if (propertyName is nameof(AppSessionStore.RunInProgress)
            or nameof(AppSessionStore.StopRequested)
            or nameof(AppSessionStore.PickerActive)
            or nameof(AppSessionStore.ComposerMode)
            or nameof(AppSessionStore.LastFault))
        {
            OnPropertyChanged(nameof(CurrentState));
        }
    }
}
