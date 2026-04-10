using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public sealed class ActionComposerViewModel : ObservableObject
{
    private readonly AppSessionStore _store;
    private readonly RunCoordinator _coordinator;
    private readonly ObservableCollection<KeyOption> _availableKeys =
    [
        new("Space", 0x20, 0x39, false),
        new("Enter", 0x0D, 0x1C, false),
        new("Esc", 0x1B, 0x01, false),
        new("Left Arrow", 0x25, 0x4B, true),
        new("Right Arrow", 0x27, 0x4D, true),
        new("Up Arrow", 0x26, 0x48, true),
        new("Down Arrow", 0x28, 0x50, true),
        new("F1", 0x70, 0x3B, false),
        new("F2", 0x71, 0x3C, false),
        new("F24", 0x87, 0x76, false),
    ];
    private readonly AsyncRelayCommand _captureCurrentCursorCommand;
    private readonly AsyncRelayCommand _pickOnScreenCommand;
    private readonly RelayCommand _confirmDraftCommand;

    public ActionComposerViewModel(AppSessionStore store, RunCoordinator coordinator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        BeginAddMouseClickCommand = new RelayCommand(BeginAddMouseClick, CanBeginEditing);
        BeginAddKeyPressCommand = new RelayCommand(BeginAddKeyPress, CanBeginEditing);
        BeginAddDelayCommand = new RelayCommand(BeginAddDelay, CanBeginEditing);
        CancelDraftCommand = new RelayCommand(CancelDraft, () => IsDraftOpen && CanBeginEditing());
        _confirmDraftCommand = new RelayCommand(ConfirmDraft, CanConfirmDraft);
        _captureCurrentCursorCommand = new AsyncRelayCommand(
            () => _coordinator.CaptureCurrentCursorAsync(triggeredByHotkey: false),
            CanCaptureCoordinate);
        _pickOnScreenCommand = new AsyncRelayCommand(
            () => _coordinator.PickCoordinateAsync(),
            CanCaptureCoordinate);

        _store.PropertyChanged += (_, args) => OnStorePropertyChanged(args.PropertyName);
    }

    public ReadOnlyObservableCollection<KeyOption> AvailableKeys => new(_availableKeys);

    public IReadOnlyList<MouseButtonKind> MouseButtonOptions { get; } = [MouseButtonKind.Left, MouseButtonKind.Right];

    public IRelayCommand BeginAddMouseClickCommand { get; }

    public IRelayCommand BeginAddKeyPressCommand { get; }

    public IRelayCommand BeginAddDelayCommand { get; }

    public IRelayCommand CancelDraftCommand { get; }

    public IRelayCommand ConfirmDraftCommand => _confirmDraftCommand;

    public IAsyncRelayCommand CaptureCurrentCursorCommand => _captureCurrentCursorCommand;

    public IAsyncRelayCommand PickOnScreenCommand => _pickOnScreenCommand;

    public MouseButtonKind SelectedMouseButton
    {
        get => _store.MouseButton;
        set
        {
            if (_store.MouseButton != value)
            {
                _store.MouseButton = value;
                OnPropertyChanged();
            }
        }
    }

    public KeyOption? SelectedKeyOption
    {
        get => _store.SelectedKeyOption;
        set
        {
            if (_store.SelectedKeyOption != value)
            {
                _store.SelectedKeyOption = value;
                OnPropertyChanged();
                _confirmDraftCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DelayMillisecondsText
    {
        get => _store.DelayMillisecondsText;
        set
        {
            if (_store.DelayMillisecondsText != value)
            {
                _store.DelayMillisecondsText = value;
                OnPropertyChanged();
                _confirmDraftCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsDraftOpen => _store.ComposerMode != ComposerMode.None;

    public bool IsMouseDraftActive => _store.ComposerMode == ComposerMode.MouseClick;

    public bool IsKeyDraftActive => _store.ComposerMode == ComposerMode.KeyPress;

    public bool IsDelayDraftActive => _store.ComposerMode == ComposerMode.Delay;

    public bool IsEditingExistingAction => _store.EditingIndex is not null;

    public string DraftTitle
        => _store.ComposerMode switch
        {
            ComposerMode.MouseClick => IsEditingExistingAction ? "Edit Mouse Click" : "Add Mouse Click",
            ComposerMode.KeyPress => IsEditingExistingAction ? "Edit Key Press" : "Add Key Press",
            ComposerMode.Delay => IsEditingExistingAction ? "Edit Delay" : "Add Delay",
            _ => "No Draft",
        };

    public string ConfirmDraftText => IsEditingExistingAction ? "Save Action" : "Add Action";

    internal void BeginEditAction(int index, AutomationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _store.LastFault = null;
        _store.EditingIndex = index;

        switch (action)
        {
            case MouseClickAction mouseClickAction:
                _store.ComposerMode = ComposerMode.MouseClick;
                _store.MouseButton = mouseClickAction.Button;
                _store.SelectedCoordinate = mouseClickAction.Position;
                break;
            case KeyPressAction keyPressAction:
                _store.ComposerMode = ComposerMode.KeyPress;
                _store.SelectedKeyOption = EnsureKeyOption(keyPressAction);
                break;
            case DelayAction delayAction:
                _store.ComposerMode = ComposerMode.Delay;
                _store.DelayMillisecondsText = ((long)Math.Round(delayAction.Duration.TotalMilliseconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
        }

        RaiseDraftPropertiesChanged();
    }

    private void BeginAddMouseClick()
    {
        _store.LastFault = null;
        _store.BeginMouseDraft();
        RaiseDraftPropertiesChanged();
    }

    private void BeginAddKeyPress()
    {
        _store.LastFault = null;
        _store.BeginKeyDraft();
        RaiseDraftPropertiesChanged();
    }

    private void BeginAddDelay()
    {
        _store.LastFault = null;
        _store.BeginDelayDraft();
        RaiseDraftPropertiesChanged();
    }

    private void CancelDraft()
    {
        _store.CloseDraft();
        RaiseDraftPropertiesChanged();
    }

    private void ConfirmDraft()
    {
        if (!TryBuildDraftAction(out var action))
        {
            return;
        }

        _store.LastFault = null;
        if (_store.EditingIndex is int editingIndex)
        {
            _store.Actions[editingIndex] = action;
        }
        else
        {
            _store.Actions.Add(action);
        }

        switch (_store.ComposerMode)
        {
            case ComposerMode.MouseClick:
                _store.SelectedCoordinate = null;
                break;
            case ComposerMode.KeyPress:
                _store.SelectedKeyOption = null;
                break;
            case ComposerMode.Delay:
                _store.DelayMillisecondsText = string.Empty;
                break;
        }

        _store.CloseDraft();
        _store.SetStatus($"{ActionSummaryFormatter.Format(action)} added to the sequence.", InfoBarSeverity.Success);
        _store.AppendLog($"Action saved: {ActionSummaryFormatter.Format(action)}.");
        RaiseDraftPropertiesChanged();
    }

    private bool TryBuildDraftAction(out AutomationAction action)
    {
        action = null!;

        switch (_store.ComposerMode)
        {
            case ComposerMode.MouseClick when _store.SelectedCoordinate is ScreenPoint point:
                action = new MouseClickAction(_store.MouseButton, point);
                return true;
            case ComposerMode.KeyPress when _store.SelectedKeyOption is not null:
                action = new KeyPressAction(
                    _store.SelectedKeyOption.VirtualKey,
                    _store.SelectedKeyOption.ScanCode,
                    _store.SelectedKeyOption.IsExtendedKey,
                    _store.SelectedKeyOption.DisplayLabel);
                return true;
            case ComposerMode.Delay when ulong.TryParse(
                _store.DelayMillisecondsText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var milliseconds):
                action = new DelayAction(TimeSpan.FromMilliseconds(milliseconds));
                return true;
            default:
                return false;
        }
    }

    private bool CanBeginEditing() => !_store.RunInProgress && !_store.StopRequested;

    private bool CanCaptureCoordinate()
        => CanBeginEditing() && _store.ComposerMode == ComposerMode.MouseClick && !_store.PickerActive;

    private bool CanConfirmDraft()
        => CanBeginEditing() && TryBuildDraftAction(out _);

    private KeyOption EnsureKeyOption(KeyPressAction action)
    {
        var existing = _availableKeys.FirstOrDefault(option =>
            option.VirtualKey == action.VirtualKey
            && option.ScanCode == action.ScanCode
            && option.IsExtendedKey == action.IsExtendedKey
            && string.Equals(option.DisplayLabel, action.DisplayLabel, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        var custom = new KeyOption(action.DisplayLabel, action.VirtualKey, action.ScanCode, action.IsExtendedKey);
        _availableKeys.Insert(0, custom);
        OnPropertyChanged(nameof(AvailableKeys));
        return custom;
    }

    private void OnStorePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(AppSessionStore.ComposerMode):
            case nameof(AppSessionStore.EditingIndex):
                RaiseDraftPropertiesChanged();
                break;
            case nameof(AppSessionStore.MouseButton):
                OnPropertyChanged(nameof(SelectedMouseButton));
                break;
            case nameof(AppSessionStore.SelectedCoordinate):
                _confirmDraftCommand.NotifyCanExecuteChanged();
                _captureCurrentCursorCommand.NotifyCanExecuteChanged();
                _pickOnScreenCommand.NotifyCanExecuteChanged();
                break;
            case nameof(AppSessionStore.SelectedKeyOption):
                OnPropertyChanged(nameof(SelectedKeyOption));
                break;
            case nameof(AppSessionStore.DelayMillisecondsText):
                OnPropertyChanged(nameof(DelayMillisecondsText));
                break;
            case nameof(AppSessionStore.RunInProgress):
            case nameof(AppSessionStore.StopRequested):
            case nameof(AppSessionStore.PickerActive):
                BeginAddMouseClickCommand.NotifyCanExecuteChanged();
                BeginAddKeyPressCommand.NotifyCanExecuteChanged();
                BeginAddDelayCommand.NotifyCanExecuteChanged();
                CancelDraftCommand.NotifyCanExecuteChanged();
                _confirmDraftCommand.NotifyCanExecuteChanged();
                _captureCurrentCursorCommand.NotifyCanExecuteChanged();
                _pickOnScreenCommand.NotifyCanExecuteChanged();
                break;
        }
    }

    private void RaiseDraftPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsDraftOpen));
        OnPropertyChanged(nameof(IsMouseDraftActive));
        OnPropertyChanged(nameof(IsKeyDraftActive));
        OnPropertyChanged(nameof(IsDelayDraftActive));
        OnPropertyChanged(nameof(IsEditingExistingAction));
        OnPropertyChanged(nameof(DraftTitle));
        OnPropertyChanged(nameof(ConfirmDraftText));
        CancelDraftCommand.NotifyCanExecuteChanged();
        _confirmDraftCommand.NotifyCanExecuteChanged();
        _captureCurrentCursorCommand.NotifyCanExecuteChanged();
        _pickOnScreenCommand.NotifyCanExecuteChanged();
    }
}
