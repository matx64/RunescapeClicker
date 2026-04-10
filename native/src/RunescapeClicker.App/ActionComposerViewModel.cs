using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using RunescapeClicker.Automation.Windows;
using RunescapeClicker.Core;
using Windows.System;

namespace RunescapeClicker.App;

public sealed class ActionComposerViewModel : ObservableObject
{
    private readonly AppSessionStore _store;
    private readonly RunCoordinator _coordinator;
    private readonly IKeyboardKeyMetadataService _keyboardKeyMetadataService;
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
    private readonly ReadOnlyObservableCollection<KeyOption> _availableKeyOptions;
    private readonly AsyncRelayCommand _captureCurrentCursorCommand;
    private readonly AsyncRelayCommand _pickOnScreenCommand;
    private readonly RelayCommand _beginKeyCaptureCommand;
    private readonly RelayCommand _clearCapturedKeyCommand;
    private readonly RelayCommand _confirmDraftCommand;

    public ActionComposerViewModel(
        AppSessionStore store,
        RunCoordinator coordinator,
        IKeyboardKeyMetadataService keyboardKeyMetadataService)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _keyboardKeyMetadataService = keyboardKeyMetadataService ?? throw new ArgumentNullException(nameof(keyboardKeyMetadataService));
        _availableKeyOptions = new ReadOnlyObservableCollection<KeyOption>(_availableKeys);

        BeginAddMouseClickCommand = new RelayCommand(BeginAddMouseClick, CanBeginEditing);
        BeginAddKeyPressCommand = new RelayCommand(BeginAddKeyPress, CanBeginEditing);
        BeginAddDelayCommand = new RelayCommand(BeginAddDelay, CanBeginEditing);
        CancelDraftCommand = new RelayCommand(CancelDraft, () => IsDraftOpen && CanBeginEditing());
        _beginKeyCaptureCommand = new RelayCommand(BeginKeyCapture, CanCaptureKey);
        _clearCapturedKeyCommand = new RelayCommand(ClearCapturedKey, CanClearCapturedKey);
        _confirmDraftCommand = new RelayCommand(ConfirmDraft, CanConfirmDraft);
        _captureCurrentCursorCommand = new AsyncRelayCommand(
            () => _coordinator.CaptureCurrentCursorAsync(triggeredByHotkey: false),
            CanCaptureCoordinate);
        _pickOnScreenCommand = new AsyncRelayCommand(
            () => _coordinator.PickCoordinateAsync(),
            CanCaptureCoordinate);

        _store.PropertyChanged += (_, args) => OnStorePropertyChanged(args.PropertyName);
    }

    public ReadOnlyObservableCollection<KeyOption> AvailableKeys => _availableKeyOptions;

    public IReadOnlyList<MouseButtonKind> MouseButtonOptions { get; } = [MouseButtonKind.Left, MouseButtonKind.Right];

    public IRelayCommand BeginAddMouseClickCommand { get; }

    public IRelayCommand BeginAddKeyPressCommand { get; }

    public IRelayCommand BeginAddDelayCommand { get; }

    public IRelayCommand CancelDraftCommand { get; }

    public IRelayCommand BeginKeyCaptureCommand => _beginKeyCaptureCommand;

    public IRelayCommand ClearCapturedKeyCommand => _clearCapturedKeyCommand;

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

    public bool IsAwaitingKeyCapture => _store.AwaitingKeyCapture;

    public string DraftTitle
        => _store.ComposerMode switch
        {
            ComposerMode.MouseClick => IsEditingExistingAction ? "Edit Mouse Click" : "Add Mouse Click",
            ComposerMode.KeyPress => IsEditingExistingAction ? "Edit Key Press" : "Add Key Press",
            ComposerMode.Delay => IsEditingExistingAction ? "Edit Delay" : "Add Delay",
            _ => "No Draft",
        };

    public string ConfirmDraftText => IsEditingExistingAction ? "Save Action" : "Add Action";

    public string CapturedKeyText
        => _store.SelectedKeyOption is not null
            ? $"Captured key: {_store.SelectedKeyOption.DisplayLabel}"
            : IsAwaitingKeyCapture
                ? "Press a key anywhere in the window to capture it."
                : "Captured key: none";

    public string KeyCaptureHintText
        => IsAwaitingKeyCapture
            ? "The next key you press will be stored with Windows virtual-key and scan-code metadata."
            : "Click Capture Key, then press the keyboard key you want this action to replay.";

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
                _store.AwaitingKeyCapture = false;
                break;
            case DelayAction delayAction:
                _store.ComposerMode = ComposerMode.Delay;
                _store.DelayMillisecondsText = ((long)Math.Round(delayAction.Duration.TotalMilliseconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
        }

        RaiseDraftPropertiesChanged();
    }

    public bool TryCaptureKey(VirtualKey key)
    {
        if (!CanCaptureKey() || !_store.AwaitingKeyCapture)
        {
            return false;
        }

        if (!_keyboardKeyMetadataService.TryCreate(key, out var metadata))
        {
            _store.SetStatus("That key could not be captured with Windows metadata.", InfoBarSeverity.Warning);
            return false;
        }

        var capturedOption = EnsureKeyOption(
            new KeyPressAction(
                metadata.VirtualKey,
                metadata.ScanCode,
                metadata.IsExtendedKey,
                metadata.DisplayLabel));

        _store.LastFault = null;
        _store.SelectedKeyOption = capturedOption;
        _store.AwaitingKeyCapture = false;
        _store.SetStatus($"Captured key {capturedOption.DisplayLabel}.", InfoBarSeverity.Success);
        _store.AppendLog(
            $"Captured key {capturedOption.DisplayLabel} (VK 0x{capturedOption.VirtualKey:X2}, scan 0x{capturedOption.ScanCode:X2}).");
        return true;
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

    private void BeginKeyCapture()
    {
        if (!CanCaptureKey())
        {
            return;
        }

        _store.AwaitingKeyCapture = true;
        _store.SetStatus("Press the keyboard key you want to automate.", InfoBarSeverity.Informational);
        RaiseDraftPropertiesChanged();
    }

    private void ClearCapturedKey()
    {
        if (!CanClearCapturedKey())
        {
            return;
        }

        _store.SelectedKeyOption = null;
        _store.AwaitingKeyCapture = true;
        _store.SetStatus("Captured key cleared. Press another key to continue.", InfoBarSeverity.Informational);
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
                _store.AwaitingKeyCapture = false;
                break;
            case ComposerMode.Delay:
                _store.DelayMillisecondsText = string.Empty;
                break;
        }

        _store.CloseDraft();
        _store.SetStatus($"{ActionSummaryFormatter.Format(action)} saved to the sequence.", InfoBarSeverity.Success);
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

    private bool CanCaptureKey()
        => CanBeginEditing() && _store.ComposerMode == ComposerMode.KeyPress;

    private bool CanClearCapturedKey()
        => CanCaptureKey() && _store.SelectedKeyOption is not null;

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
            case nameof(AppSessionStore.AwaitingKeyCapture):
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
                OnPropertyChanged(nameof(CapturedKeyText));
                _clearCapturedKeyCommand.NotifyCanExecuteChanged();
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
                _beginKeyCaptureCommand.NotifyCanExecuteChanged();
                _clearCapturedKeyCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(IsAwaitingKeyCapture));
        OnPropertyChanged(nameof(DraftTitle));
        OnPropertyChanged(nameof(ConfirmDraftText));
        OnPropertyChanged(nameof(CapturedKeyText));
        OnPropertyChanged(nameof(KeyCaptureHintText));
        CancelDraftCommand.NotifyCanExecuteChanged();
        _confirmDraftCommand.NotifyCanExecuteChanged();
        _captureCurrentCursorCommand.NotifyCanExecuteChanged();
        _pickOnScreenCommand.NotifyCanExecuteChanged();
        _beginKeyCaptureCommand.NotifyCanExecuteChanged();
        _clearCapturedKeyCommand.NotifyCanExecuteChanged();
    }
}
