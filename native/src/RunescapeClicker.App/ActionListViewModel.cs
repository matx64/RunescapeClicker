using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RunescapeClicker.App;

public sealed class ActionListViewModel : ObservableObject
{
    private readonly AppSessionStore _store;
    private readonly ActionComposerViewModel _composerViewModel;
    private readonly ObservableCollection<ActionListItemViewModel> _items = [];
    private readonly RelayCommand _clearActionsCommand;

    public ActionListViewModel(AppSessionStore store, ActionComposerViewModel composerViewModel)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _composerViewModel = composerViewModel ?? throw new ArgumentNullException(nameof(composerViewModel));
        Items = new ReadOnlyObservableCollection<ActionListItemViewModel>(_items);
        _clearActionsCommand = new RelayCommand(ClearActions, () => CanModifyActions && _store.HasActions);

        _store.Actions.CollectionChanged += (_, _) =>
        {
            RebuildItems();
            OnPropertyChanged(nameof(HasActions));
            _clearActionsCommand.NotifyCanExecuteChanged();
        };

        _store.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AppSessionStore.RunInProgress) or nameof(AppSessionStore.StopRequested))
            {
                _clearActionsCommand.NotifyCanExecuteChanged();
                RefreshItemCommands();
            }
        };
    }

    public ReadOnlyObservableCollection<ActionListItemViewModel> Items { get; }

    public IRelayCommand ClearActionsCommand => _clearActionsCommand;

    public bool HasActions => _store.HasActions;

    public bool CanModifyActions => !_store.RunInProgress && !_store.StopRequested;

    internal int Count => _store.Actions.Count;

    internal void EditAction(int index)
    {
        if (!CanModifyActions || index < 0 || index >= _store.Actions.Count)
        {
            return;
        }

        _store.LastFault = null;
        _composerViewModel.BeginEditAction(index, _store.Actions[index]);
    }

    internal void DeleteAction(int index)
    {
        if (!CanModifyActions || index < 0 || index >= _store.Actions.Count)
        {
            return;
        }

        _store.LastFault = null;
        AdjustEditingIndexAfterDelete(index);
        _store.Actions.RemoveAt(index);
        _store.SetStatus("Action removed from the sequence.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
    }

    internal void MoveAction(int index, int delta)
    {
        if (!CanModifyActions)
        {
            return;
        }

        var newIndex = index + delta;
        if (index < 0 || index >= _store.Actions.Count || newIndex < 0 || newIndex >= _store.Actions.Count)
        {
            return;
        }

        _store.LastFault = null;
        (_store.Actions[index], _store.Actions[newIndex]) = (_store.Actions[newIndex], _store.Actions[index]);
        AdjustEditingIndexAfterMove(index, newIndex);
        RebuildItems();
    }

    private void ClearActions()
    {
        if (!CanModifyActions || !_store.HasActions)
        {
            return;
        }

        _store.LastFault = null;
        if (_store.EditingIndex is not null)
        {
            _store.CloseDraft();
        }

        _store.Actions.Clear();
        _store.SetStatus("Sequence cleared.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
    }

    private void RebuildItems()
    {
        _items.Clear();
        for (var index = 0; index < _store.Actions.Count; index++)
        {
            _items.Add(new ActionListItemViewModel(this, index, _store.Actions[index]));
        }
    }

    private void RefreshItemCommands()
    {
        foreach (var item in _items)
        {
            item.RefreshCommands();
        }
    }

    private void AdjustEditingIndexAfterDelete(int deletedIndex)
    {
        if (_store.EditingIndex is not int editingIndex)
        {
            return;
        }

        if (editingIndex == deletedIndex)
        {
            _store.CloseDraft();
            return;
        }

        if (editingIndex > deletedIndex)
        {
            _store.EditingIndex = editingIndex - 1;
        }
    }

    private void AdjustEditingIndexAfterMove(int sourceIndex, int destinationIndex)
    {
        if (_store.EditingIndex is not int editingIndex)
        {
            return;
        }

        if (editingIndex == sourceIndex)
        {
            _store.EditingIndex = destinationIndex;
            return;
        }

        if (editingIndex == destinationIndex)
        {
            _store.EditingIndex = sourceIndex;
        }
    }
}
