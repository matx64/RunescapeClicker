using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public sealed class ActionListItemViewModel : ObservableObject
{
    private readonly ActionListViewModel _owner;
    private int _index;
    private AutomationAction _action;

    public ActionListItemViewModel(ActionListViewModel owner, int index, AutomationAction action)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _index = index;
        _action = action ?? throw new ArgumentNullException(nameof(action));
        EditCommand = new RelayCommand(() => _owner.EditAction(_index), CanModify);
        DeleteCommand = new RelayCommand(() => _owner.DeleteAction(_index), CanModify);
        MoveUpCommand = new RelayCommand(() => _owner.MoveAction(_index, -1), CanMoveUp);
        MoveDownCommand = new RelayCommand(() => _owner.MoveAction(_index, 1), CanMoveDown);
    }

    public AutomationAction Action
    {
        get => _action;
        private set
        {
            if (SetProperty(ref _action, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public int Index
    {
        get => _index;
        private set
        {
            if (SetProperty(ref _index, value))
            {
                OnPropertyChanged(nameof(DisplayIndex));
                RefreshCommands();
            }
        }
    }

    public string DisplayIndex => (Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string Summary => ActionSummaryFormatter.Format(Action);

    public IRelayCommand EditCommand { get; }

    public IRelayCommand DeleteCommand { get; }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public void Update(int index, AutomationAction action)
    {
        Index = index;
        Action = action;
    }

    public void RefreshCommands()
    {
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanModify() => _owner.CanModifyActions;

    private bool CanMoveUp() => _owner.CanModifyActions && Index > 0;

    private bool CanMoveDown() => _owner.CanModifyActions && Index < _owner.Count - 1;
}
