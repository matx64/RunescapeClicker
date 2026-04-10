using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public sealed class AppSessionStore : ObservableObject
{
    private readonly StringBuilder _logBuilder = new();
    private ComposerMode _composerMode;
    private int? _editingIndex;
    private MouseButtonKind _mouseButton = MouseButtonKind.Left;
    private ScreenPoint? _selectedCoordinate;
    private KeyOption? _selectedKeyOption;
    private bool _awaitingKeyCapture;
    private string _delayMillisecondsText = string.Empty;
    private StopRuleMode _stopRuleMode;
    private string _timerSecondsText = "120";
    private TimeSpan _lastValidTimerDuration = TimeSpan.FromSeconds(120);
    private StopCondition _currentStopCondition = StopCondition.HotkeyOnly;
    private bool _pickerActive;
    private bool _runInProgress;
    private bool _stopRequested;
    private bool _hotkeysRegistered;
    private string _hotkeyStatusText = "Hotkeys not initialized.";
    private string _lastHotkeyText = "Last hotkey event: none";
    private string _runStatusText = "Run status: idle";
    private string _statusMessage = "Initializing the Phase 6 WinUI shell...";
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    private string _logText = string.Empty;
    private EngineError? _lastFault;
    private RunRequest? _activeRunRequest;
    private RunRequest? _lastRequestedRun;
    private RunResult? _lastRunResult;

    public AppSessionStore()
    {
        Actions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasActions));
    }

    public ObservableCollection<AutomationAction> Actions { get; } = [];

    public ComposerMode ComposerMode
    {
        get => _composerMode;
        set => SetProperty(ref _composerMode, value);
    }

    public int? EditingIndex
    {
        get => _editingIndex;
        set => SetProperty(ref _editingIndex, value);
    }

    public MouseButtonKind MouseButton
    {
        get => _mouseButton;
        set => SetProperty(ref _mouseButton, value);
    }

    public ScreenPoint? SelectedCoordinate
    {
        get => _selectedCoordinate;
        set
        {
            if (SetProperty(ref _selectedCoordinate, value))
            {
                OnPropertyChanged(nameof(HasSelectedCoordinate));
                OnPropertyChanged(nameof(PickedCoordinateText));
            }
        }
    }

    public KeyOption? SelectedKeyOption
    {
        get => _selectedKeyOption;
        set => SetProperty(ref _selectedKeyOption, value);
    }

    public bool AwaitingKeyCapture
    {
        get => _awaitingKeyCapture;
        set => SetProperty(ref _awaitingKeyCapture, value);
    }

    public string DelayMillisecondsText
    {
        get => _delayMillisecondsText;
        set => SetProperty(ref _delayMillisecondsText, value);
    }

    public StopRuleMode StopRuleMode
    {
        get => _stopRuleMode;
        private set
        {
            if (SetProperty(ref _stopRuleMode, value))
            {
                OnPropertyChanged(nameof(IsTimerStopEnabled));
            }
        }
    }

    public string TimerSecondsText
    {
        get => _timerSecondsText;
        private set => SetProperty(ref _timerSecondsText, value);
    }

    public StopCondition CurrentStopCondition
    {
        get => _currentStopCondition;
        private set => SetProperty(ref _currentStopCondition, value);
    }

    public bool PickerActive
    {
        get => _pickerActive;
        set => SetProperty(ref _pickerActive, value);
    }

    public bool RunInProgress
    {
        get => _runInProgress;
        set => SetProperty(ref _runInProgress, value);
    }

    public bool StopRequested
    {
        get => _stopRequested;
        set => SetProperty(ref _stopRequested, value);
    }

    public bool HotkeysRegistered
    {
        get => _hotkeysRegistered;
        set => SetProperty(ref _hotkeysRegistered, value);
    }

    public string HotkeyStatusText
    {
        get => _hotkeyStatusText;
        set => SetProperty(ref _hotkeyStatusText, value);
    }

    public string LastHotkeyText
    {
        get => _lastHotkeyText;
        set => SetProperty(ref _lastHotkeyText, value);
    }

    public string RunStatusText
    {
        get => _runStatusText;
        set => SetProperty(ref _runStatusText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public InfoBarSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public EngineError? LastFault
    {
        get => _lastFault;
        set => SetProperty(ref _lastFault, value);
    }

    public RunRequest? ActiveRunRequest
    {
        get => _activeRunRequest;
        set => SetProperty(ref _activeRunRequest, value);
    }

    public RunRequest? LastRequestedRun
    {
        get => _lastRequestedRun;
        set => SetProperty(ref _lastRequestedRun, value);
    }

    public RunResult? LastRunResult
    {
        get => _lastRunResult;
        set => SetProperty(ref _lastRunResult, value);
    }

    public bool HasActions => Actions.Count > 0;

    public bool HasSelectedCoordinate => SelectedCoordinate is not null;

    public bool IsTimerStopEnabled => StopRuleMode == StopRuleMode.Timer;

    public string PickedCoordinateText
        => SelectedCoordinate is ScreenPoint point
            ? string.Create(CultureInfo.InvariantCulture, $"Picked coordinate: ({point.X}, {point.Y})")
            : "Picked coordinate: none";

    public void BeginMouseDraft()
    {
        ComposerMode = ComposerMode.MouseClick;
        EditingIndex = null;
        SelectedCoordinate = null;
        AwaitingKeyCapture = false;
    }

    public void BeginKeyDraft()
    {
        ComposerMode = ComposerMode.KeyPress;
        EditingIndex = null;
        SelectedKeyOption = null;
        AwaitingKeyCapture = true;
    }

    public void BeginDelayDraft()
    {
        ComposerMode = ComposerMode.Delay;
        EditingIndex = null;
        DelayMillisecondsText = string.Empty;
        AwaitingKeyCapture = false;
    }

    public void CloseDraft()
    {
        ComposerMode = ComposerMode.None;
        EditingIndex = null;
        AwaitingKeyCapture = false;
    }

    public void SetStopRuleMode(StopRuleMode mode)
    {
        StopRuleMode = mode;
        CurrentStopCondition = mode == StopRuleMode.HotkeyOnly
            ? StopCondition.HotkeyOnly
            : StopCondition.Timer(_lastValidTimerDuration);
    }

    public bool TryUpdateTimerSeconds(string text)
    {
        TimerSecondsText = text;
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        _lastValidTimerDuration = TimeSpan.FromSeconds(seconds);
        if (StopRuleMode == StopRuleMode.Timer)
        {
            CurrentStopCondition = StopCondition.Timer(_lastValidTimerDuration);
        }

        return true;
    }

    public void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    public void AppendLog(string message)
    {
        if (_logBuilder.Length > 0)
        {
            _logBuilder.AppendLine();
        }

        _logBuilder.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.Now:HH:mm:ss}  {message}"));
        LogText = _logBuilder.ToString();
    }
}
